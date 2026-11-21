#!/usr/bin/env python3
"""Seleção dos testes das rodadas intermediárias de uma change, a partir do diff.

Uso, a partir de qualquer diretório:

    python3 scripts/selecionar-testes.py                # o plano, em texto
    python3 scripts/selecionar-testes.py --passos       # uma linha por passo: id, diretório, comando
    python3 scripts/selecionar-testes.py --base <ref>   # outra base que não o origin/main
    python3 scripts/selecionar-testes.py --caminhos -   # a lista de caminhos tocados vem da entrada

O script só PLANEJA: não roda dotnet test nem npm, e não toma o lock das suítes. Quem executa os passos, um por vez,
é quem já tem o lock e a regra de validade da rodada (docs/development.md, "Rodar os testes"). A baseline e o
fechamento de uma change continuam completos (convenção 19 do 01-ARQUITETURA_E_CONVENCOES.md); esta seleção vale
só para as rodadas do meio.

As regras estão no design da change selecao-de-testes-por-diff (#307), D4, e no docs/development.md. A expectativa
delas está escrita à parte, em scripts/tests/selecao-esperada.md, e os testes conferem o plano contra ela.

O erro vai sempre para o lado de rodar mais: caminho que nenhuma regra reconhece roda tudo.
"""

from __future__ import annotations

import argparse
import os
import re
import shlex
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath, PureWindowsPath

REPO = Path(__file__).resolve().parent.parent

APPS_DOTNET = ("api", "workers", "inbox", "connectors")

# Regra 1: documentação, pelo LUGAR e não pela extensão (há .md lidos por teste em apps/connectors/tests/Fixtures).
# .claude/ são os comandos e skills do assistente, e o .gitignore da raiz é configuração do git: nenhum teste lê esses
# arquivos (o G4 dos testes acusa se mudar). O .gitignore muda a lista de arquivos que esta seleção enxerga, e os
# testes do script, que entram em todo plano, cobrem isso.
DOCUMENTACAO = re.compile(r"^(docs/|openspec/|\.github/|\.claude/|\.gitignore$|[^/]+\.md$|LICENSE$|NOTICE$)")

# Regra 5: o núcleo do frontend, onde o Vitest inteiro roda no lugar do related.
NUCLEO_FRONTEND = re.compile(r"^apps/frontend/src/(auth/|app/|theme\.ts$)")

# Artefatos lidos por outra pilha (linha 4 e acréscimos A1/A2 da D4). Cada leitor é um arquivo que tem de existir:
# se sumir ou mudar de lugar, o plano falha em vez de seguir sem o passo.
LEITOR_NGINX_DOTNET = "tests/Shared/NginxRouting.cs"  # linkado por Compile Include nos projetos que o usam
FILTRO_NGINX = "FullyQualifiedName~Nginx"
LEITOR_NGINX_FRONTEND = "apps/frontend/src/app/nginxHeaders.test.ts"
LEITOR_VERSAO = "apps/api/tests/Buteco.Api.Tests/ProductVersionCoherenceTests.cs"
FILTRO_VERSAO = "FullyQualifiedName~ProductVersionCoherenceTests"

# Os leitores, para o G4 dos testes: quem lê arquivo de fora do próprio projeto, e por qual regra é alcançado.
LEITURAS_CRUZADAS = {
    LEITOR_NGINX_DOTNET: "linha 4 (apps/frontend/deploy/**)",
    LEITOR_NGINX_FRONTEND: "linha 4 (apps/frontend/deploy/**) e linha 7 (index.html)",
    LEITOR_VERSAO: "acréscimos A1 (*.csproj) e A2 (apps/frontend/package.json); Directory.Build.props cai em tudo",
}

FRONTEND = "apps/frontend"
SCRIPTS_TESTES = "scripts/tests"

# Ordem dos passos no plano: os baratos e determinísticos primeiro.
ORDEM = {
    "check-docs": 0,
    "scripts-testes": 1,
    "fe-tsc": 2,
    "fe-lint": 3,
    "dotnet-filtrado": 4,
    "fe-arquivos": 5,
    "fe-vitest": 6,
    "dotnet-app": 7,
    "dotnet-tests": 8,
}


class ErroDeSelecao(Exception):
    """Falha que não pode virar plano vazio nem plano menor: o script sai com 2 e nomeia o motivo."""


# --------------------------------------------------------------------------------------------------------------
# Os caminhos tocados (D3)


def _git(repo: Path, *args: str) -> str:
    feito = subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True)
    if feito.returncode != 0:
        raise ErroDeSelecao(f"git {' '.join(args)} falhou: {feito.stderr.strip()}")
    return feito.stdout


@dataclass
class Base:
    ref: str
    sha: str
    merge_base: str


def caminhos_do_diff(repo: Path, ref: str) -> tuple[Base, list[str]]:
    """O diff do merge-base entre a base e o HEAD até a árvore de trabalho, mais os arquivos novos não rastreados."""
    try:
        sha = _git(repo, "rev-parse", "--verify", f"{ref}^{{commit}}").strip()
    except ErroDeSelecao as erro:
        raise ErroDeSelecao(f"a base '{ref}' não existe neste clone (falta fetch?): {erro}") from None
    try:
        merge_base = _git(repo, "merge-base", sha, "HEAD").strip()
    except ErroDeSelecao as erro:
        raise ErroDeSelecao(
            f"sem merge-base entre '{ref}' e HEAD (clone raso? histórico cortado?): {erro}"
        ) from None
    caminhos: set[str] = set()
    campos = _git(repo, "diff", "--name-status", "-M", "-z", merge_base).split("\0")
    i = 0
    while i < len(campos) and campos[i]:
        status = campos[i]
        if status[0] in "RC":  # renomeação e cópia trazem dois caminhos: os dois contam
            caminhos.update(campos[i + 1 : i + 3])
            i += 3
        else:
            caminhos.add(campos[i + 1])
            i += 2
    caminhos.update(p for p in _git(repo, "ls-files", "--others", "--exclude-standard", "-z").split("\0") if p)
    return Base(ref, sha, merge_base), sorted(caminhos)


# --------------------------------------------------------------------------------------------------------------
# O grafo dos projetos .NET (D5)


@dataclass
class Projeto:
    caminho: str  # o .csproj, relativo à raiz
    teste: bool
    referencias: list[str] = field(default_factory=list)  # outros .csproj
    linkados: list[str] = field(default_factory=list)  # arquivos de fora do projeto, por Compile Include … Link

    @property
    def diretorio(self) -> str:
        return str(PurePosixPath(self.caminho).parent)

    @property
    def nome(self) -> str:
        return PurePosixPath(self.caminho).stem


def _relativo(projeto: str, incluido: str) -> str:
    alvo = PurePosixPath(projeto).parent / PureWindowsPath(incluido).as_posix()
    return os.path.normpath(str(alvo)).replace(os.sep, "/")


def arquivos_do_repositorio(repo: Path) -> list[str]:
    """Os versionados e os novos não ignorados. Fora: bin/obj e as worktrees, que o .gitignore já tira."""
    return sorted(p for p in _git(repo, "ls-files", "-co", "--exclude-standard", "-z").split("\0") if p)


def carregar_projetos(repo: Path, arquivos: list[str]) -> dict[str, Projeto]:
    projetos: dict[str, Projeto] = {}
    for caminho in arquivos:
        if not caminho.endswith(".csproj") or not (repo / caminho).is_file():
            continue
        texto = (repo / caminho).read_text(encoding="utf-8")
        # Projeto de teste é o que referencia o SDK de teste, e não o que tem "Tests" no nome.
        teste = re.search(r'<PackageReference\s+Include="Microsoft\.NET\.Test\.Sdk"', texto) is not None
        referencias = [_relativo(caminho, m) for m in re.findall(r'<ProjectReference\s+Include="([^"]+)"', texto)]
        linkados = [
            _relativo(caminho, m)
            for m in re.findall(r'<Compile\s+Include="([^"]+)"[^>]*\bLink=', texto)
            if not _relativo(caminho, m).startswith(str(PurePosixPath(caminho).parent) + "/")
        ]
        projetos[caminho] = Projeto(caminho, teste, referencias, linkados)
    return projetos


def fecho(projetos: dict[str, Projeto], inicio: str) -> set[str]:
    vistos: set[str] = set()
    pilha = [inicio]
    while pilha:
        atual = pilha.pop()
        for ref in projetos.get(atual, Projeto(atual, False)).referencias:
            if ref not in vistos:
                vistos.add(ref)
                pilha.append(ref)
    return vistos


def _dentro(caminho: str, diretorio: str) -> bool:
    return caminho == diretorio or caminho.startswith(diretorio + "/")


# --------------------------------------------------------------------------------------------------------------
# A classificação (D4): a primeira linha que casa


def classificar(caminho: str, existe: bool) -> str:
    if DOCUMENTACAO.match(caminho):
        return "1-documentacao"
    partes = caminho.split("/")
    if partes[0] == "apps" and len(partes) > 2 and partes[1] in APPS_DOTNET:
        return "2-app-dotnet"
    if partes[0] == "tests" and len(partes) > 2 and partes[1] != "Shared":
        return "3-tests"
    if caminho.startswith(FRONTEND + "/deploy/"):
        return "4-frontend-deploy"
    if NUCLEO_FRONTEND.match(caminho) or (caminho.startswith(FRONTEND + "/src/") and not existe):
        return "5-frontend-nucleo"
    if caminho.startswith(FRONTEND + "/src/"):
        return "6-frontend-src"
    if caminho.startswith(FRONTEND + "/"):
        return "7-frontend-resto"
    if partes[0] == "scripts" and len(partes) > 1:
        return "8-scripts"
    return "tudo"


def acrescimos(caminho: str) -> list[str]:
    extras = []
    if caminho.endswith(".csproj"):
        extras.append("A1-csproj")
    if caminho == FRONTEND + "/package.json":
        extras.append("A2-package-json")
    return extras


# --------------------------------------------------------------------------------------------------------------
# O plano


@dataclass
class Passo:
    id: str
    grupo: str
    diretorio: str
    comando: list[str]
    motivos: list[str] = field(default_factory=list)

    def linha(self) -> str:
        return f"{self.id}\t{self.diretorio}\t{shlex.join(self.comando)}"


@dataclass
class Plano:
    passos: list[Passo]
    por_regra: dict[str, list[str]]
    tudo: bool


class _Montagem:
    def __init__(self) -> None:
        self.passos: dict[str, Passo] = {}

    def add(self, id_: str, grupo: str, diretorio: str, comando: list[str], motivo: str) -> None:
        passo = self.passos.get(id_)
        if passo is None:
            passo = self.passos[id_] = Passo(id_, grupo, diretorio, comando)
        if motivo not in passo.motivos:
            passo.motivos.append(motivo)


def _exigir(repo: Path, caminho: str) -> None:
    if not (repo / caminho).is_file():
        raise ErroDeSelecao(f"o leitor '{caminho}' que a seleção usa não existe mais; corrija as regras do script")


def _dono(projetos: dict[str, Projeto], arquivo: str) -> list[Projeto]:
    """Os projetos de teste que compilam o arquivo: o que o contém, ou os que o linkam."""
    donos = [p for p in projetos.values() if p.teste and (_dentro(arquivo, p.diretorio) or arquivo in p.linkados)]
    if not donos:
        raise ErroDeSelecao(f"nenhum projeto de teste compila '{arquivo}'; corrija as regras do script")
    return sorted(donos, key=lambda p: p.caminho)


def varreduras_do_frontend(repo: Path, arquivos: list[str]) -> list[str]:
    """Testes do frontend que varrem src/ por readdirSync: guardas que o related não alcança, porque não importam o
    que varrem. Rodam junto de toda seleção por related."""
    achados = []
    for caminho in arquivos:
        if caminho.startswith(FRONTEND + "/src/") and re.search(r"\.test\.tsx?$", caminho):
            arquivo = repo / caminho
            if arquivo.is_file() and "readdirSync" in arquivo.read_text(encoding="utf-8"):
                achados.append(caminho)
    return sorted(achados)


def _relativo_ao_frontend(caminho: str) -> str:
    return caminho[len(FRONTEND) + 1 :]


def _dotnet_completo(m: _Montagem, projeto: Projeto, motivo: str) -> None:
    grupo = "dotnet-tests" if projeto.caminho.startswith("tests/") else "dotnet-app"
    m.add(projeto.nome, grupo, ".", ["dotnet", "test", projeto.caminho], motivo)


def _dotnet_filtrado(m: _Montagem, projeto: Projeto, rotulo: str, filtro: str, motivo: str) -> None:
    m.add(f"{projeto.nome}[{rotulo}]", "dotnet-filtrado", ".",
          ["dotnet", "test", projeto.caminho, "--filter", filtro], motivo)


def planejar(repo: Path, caminhos: list[str], arquivos: list[str] | None = None) -> Plano:
    arquivos = arquivos_do_repositorio(repo) if arquivos is None else arquivos
    projetos = carregar_projetos(repo, arquivos)
    testes = sorted((p for p in projetos.values() if p.teste), key=lambda p: p.caminho)

    por_regra: dict[str, list[str]] = {}
    for caminho in caminhos:
        regra = classificar(caminho, (repo / caminho).exists())
        por_regra.setdefault(regra, []).append(caminho)
        for extra in acrescimos(caminho):
            por_regra.setdefault(extra, []).append(caminho)

    m = _Montagem()
    m.add("check-docs", "check-docs", ".", ["python3", "scripts/check-docs.py"], "todo plano")
    m.add("scripts-testes", "scripts-testes", ".",
          ["python3", "-B", "-m", "unittest", "discover", "-s", SCRIPTS_TESTES], "todo plano")

    tudo = "tudo" in por_regra
    if tudo:
        motivo = "tudo: caminho não reconhecido (" + ", ".join(por_regra["tudo"]) + ")"
        for projeto in testes:
            _dotnet_completo(m, projeto, motivo)
        m.add("fe-tsc", "fe-tsc", FRONTEND, ["npx", "tsc", "-b"], motivo)
        m.add("fe-lint", "fe-lint", FRONTEND, ["npm", "run", "lint"], motivo)
        m.add("fe-completa", "fe-vitest", FRONTEND, ["npm", "test"], motivo)
    else:
        _planejar_por_regra(repo, m, por_regra, projetos, testes, arquivos)

    passos = list(m.passos.values())
    completos = {p.id for p in passos if p.grupo in ("dotnet-app", "dotnet-tests")}
    # O passo filtrado some quando a suíte inteira do mesmo projeto já está no plano.
    passos = [p for p in passos if not (p.grupo == "dotnet-filtrado" and p.id.split("[")[0] in completos)]
    if any(p.id == "fe-completa" for p in passos):
        passos = [p for p in passos if p.grupo != "fe-arquivos" and p.id != "fe-related"]
    passos.sort(key=lambda p: (ORDEM[p.grupo], p.id))
    return Plano(passos, por_regra, tudo)


def _planejar_por_regra(repo: Path, m: _Montagem, por_regra: dict[str, list[str]],
                        projetos: dict[str, Projeto], testes: list[Projeto], arquivos: list[str]) -> None:
    # Linhas 2 e 3: o diretório do app ou do projeto de tests/ tocado; o grafo diz quem depende dele.
    tocados: dict[str, str] = {}
    for caminho in por_regra.get("2-app-dotnet", []):
        tocados.setdefault("/".join(caminho.split("/")[:2]), "linha 2")
    for caminho in por_regra.get("3-tests", []):
        tocados.setdefault("/".join(caminho.split("/")[:2]), "linha 3")
    for projeto in testes:
        diretorios = {projeto.diretorio} | {str(PurePosixPath(r).parent) for r in fecho(projetos, projeto.caminho)}
        for diretorio, linha in sorted(tocados.items()):
            if any(_dentro(d, diretorio) for d in diretorios) or any(_dentro(f, diretorio) for f in projeto.linkados):
                via = "o próprio projeto" if _dentro(projeto.diretorio, diretorio) else "o grafo"
                _dotnet_completo(m, projeto, f"{linha}: {diretorio} tocado ({via})")

    # Linha 4: o nginx.conf é lido por testes .NET de três apps e por um teste do frontend.
    if "4-frontend-deploy" in por_regra:
        motivo = "linha 4: " + ", ".join(por_regra["4-frontend-deploy"])
        _exigir(repo, LEITOR_NGINX_DOTNET)
        for projeto in _dono(projetos, LEITOR_NGINX_DOTNET):
            _dotnet_filtrado(m, projeto, "Nginx", FILTRO_NGINX, motivo)
        _exigir(repo, LEITOR_NGINX_FRONTEND)
        m.add("fe-nginx", "fe-arquivos", FRONTEND,
              ["npx", "vitest", "run", _relativo_ao_frontend(LEITOR_NGINX_FRONTEND)], motivo)

    # Acréscimos A1 e A2: o teste de versão do api lê todo .csproj e o package.json do frontend.
    extras = por_regra.get("A1-csproj", []) + por_regra.get("A2-package-json", [])
    if extras:
        _exigir(repo, LEITOR_VERSAO)
        for projeto in _dono(projetos, LEITOR_VERSAO):
            _dotnet_filtrado(m, projeto, "Versao", FILTRO_VERSAO, "acréscimo A1/A2: " + ", ".join(extras))

    # Linhas 5 a 7: o frontend.
    nucleo = por_regra.get("5-frontend-nucleo", []) + por_regra.get("7-frontend-resto", [])
    folha = por_regra.get("6-frontend-src", [])
    if nucleo or folha:
        motivo_fe = "frontend tocado: " + ", ".join(nucleo + folha)
        m.add("fe-tsc", "fe-tsc", FRONTEND, ["npx", "tsc", "-b"], motivo_fe)
        m.add("fe-lint", "fe-lint", FRONTEND, ["npm", "run", "lint"], motivo_fe)
    if nucleo:
        m.add("fe-completa", "fe-vitest", FRONTEND, ["npm", "test"],
              "linhas 5/7 (núcleo, configuração ou arquivo apagado): " + ", ".join(nucleo))
    elif folha:
        m.add("fe-related", "fe-vitest", FRONTEND,
              ["npx", "vitest", "related", "--run", *[_relativo_ao_frontend(c) for c in folha]],
              "linha 6: " + ", ".join(folha))
        varreduras = varreduras_do_frontend(repo, arquivos)
        if varreduras:
            m.add("fe-varreduras", "fe-arquivos", FRONTEND,
                  ["npx", "vitest", "run", *[_relativo_ao_frontend(v) for v in varreduras]],
                  "linha 6: varreduras de src/ que o related não alcança")


# --------------------------------------------------------------------------------------------------------------
# A saída (D8)


def texto(plano: Plano, base: Base | None) -> str:
    linhas = ["Seleção de testes das rodadas intermediárias (scripts/selecionar-testes.py)"]
    if base is None:
        linhas.append("base: caminhos dados por --caminhos (sem diff)")
    else:
        linhas.append(f"base: {base.ref} = {base.sha[:12]}; merge-base com HEAD = {base.merge_base[:12]} (sem fetch)")
    total = sum(len(v) for k, v in plano.por_regra.items() if not k.startswith("A"))
    linhas.append(f"caminhos tocados: {total}")
    for regra in sorted(plano.por_regra):
        linhas.append(f"  [{regra}]")
        linhas.extend(f"    {c}" for c in plano.por_regra[regra])
    linhas.append(f"passos ({len(plano.passos)}), um por vez:" + ("  -- TUDO" if plano.tudo else ""))
    for n, passo in enumerate(plano.passos, 1):
        linhas.append(f"  {n}. {passo.id}  (em {passo.diretorio})")
        linhas.append(f"     $ {shlex.join(passo.comando)}")
        for motivo in passo.motivos:
            linhas.append(f"     porque: {motivo}")
    return "\n".join(linhas) + "\n"


def main(argv: list[str] | None = None) -> int:
    args = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    args.add_argument("--base", default="origin/main", help="a ref contra a qual o diff é calculado")
    args.add_argument("--caminhos", help="arquivo com um caminho por linha ('-' para a entrada), no lugar do diff")
    args.add_argument("--passos", action="store_true", help="uma linha por passo: id<TAB>diretório<TAB>comando")
    opcoes = args.parse_args(argv)
    try:
        if opcoes.caminhos:
            fonte = sys.stdin if opcoes.caminhos == "-" else open(opcoes.caminhos, encoding="utf-8")
            with fonte:
                caminhos = sorted({linha.strip() for linha in fonte if linha.strip()})
            base = None
        else:
            base, caminhos = caminhos_do_diff(REPO, opcoes.base)
        plano = planejar(REPO, caminhos)
    except ErroDeSelecao as erro:
        print(f"selecionar-testes: {erro}", file=sys.stderr)
        return 2
    if opcoes.passos:
        sys.stdout.write("".join(p.linha() + "\n" for p in plano.passos))
    else:
        sys.stdout.write(texto(plano, base))
    return 0


if __name__ == "__main__":
    sys.exit(main())
