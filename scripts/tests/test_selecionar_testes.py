"""Testes do scripts/selecionar-testes.py (change selecao-de-testes-por-diff, #307, design D7).

    python3 -B -m unittest discover -s scripts/tests

Rodam sobre a árvore real do repositório, sem subir contêiner nem rodar suíte. Entram em todo plano da seleção e no
checklist antes do PR, porque o G3 e o G4 acusam mudanças em apps/ e tests/, e não só em scripts/.

- G1: o plano de cada linha de selecao-esperada.md, a tabela escrita à parte.
- G2: os 13 fixes de fixes-da-amostra.md, pelo diff de produção de cada um.
- G3: todo projeto de teste do repositório é alcançado por uma regra.
- G4: todo teste que lê arquivo de fora do próprio projeto tem regra que o alcance.

SELECIONAR_TESTES_SCRIPT, se definida, aponta outro arquivo do script (uma cópia mutada, para provar que um guarda
reprova contra o defeito; convenção 15). Sem ela, o script versionado.
"""

from __future__ import annotations

import importlib.util
import os
import re
import subprocess
import sys
import unittest
from pathlib import Path

AQUI = Path(__file__).resolve().parent
REPO = AQUI.parent.parent
SCRIPT = Path(os.environ.get("SELECIONAR_TESTES_SCRIPT", REPO / "scripts" / "selecionar-testes.py"))


def _carregar():
    spec = importlib.util.spec_from_file_location("selecionar_testes", SCRIPT)
    modulo = importlib.util.module_from_spec(spec)
    sys.modules["selecionar_testes"] = modulo
    spec.loader.exec_module(modulo)
    return modulo


sys.dont_write_bytecode = True  # o script carregado não deixa __pycache__ em scripts/
sel = _carregar()
ARQUIVOS = sel.arquivos_do_repositorio(REPO)
SEMPRE = {"check-docs", "scripts-testes"}


def _plano(caminhos):
    return sel.planejar(REPO, caminhos, ARQUIVOS)


def _ids(plano):
    return {p.id for p in plano.passos}


def _tabela(arquivo: Path) -> tuple[dict[str, set[str]], list[list[str]]]:
    """Os conjuntos nomeados (`NOME` = `a`, `b`) e as linhas da tabela markdown, sem cabeçalho."""
    texto = arquivo.read_text(encoding="utf-8")
    conjuntos = {
        m.group(1): set(re.findall(r"`([^`]+)`", m.group(2)))
        for m in re.finditer(r"^`([A-Z-]+)` = (.+?)\n\n", texto, re.M | re.S)
    }
    linhas = []
    for linha in texto.splitlines():
        if linha.startswith("|") and not linha.startswith("|---"):
            celulas = [c.strip() for c in linha.strip("|").split("|")]
            if not celulas[0].startswith("`"):
                continue  # cabeçalho
            linhas.append(celulas)
    return conjuntos, linhas


class G1TabelaAParte(unittest.TestCase):
    def test_cada_linha_da_tabela(self):
        conjuntos, linhas = _tabela(AQUI / "selecao-esperada.md")
        self.assertGreater(len(linhas), 40, "a tabela à parte encolheu")
        for caminho_cel, esperado_cel, porque in linhas:
            caminhos = re.findall(r"`([^`]+)`", caminho_cel)
            esperado = set()
            for nome, valor in conjuntos.items():
                if re.search(rf"\b{nome}\b", esperado_cel):
                    esperado |= valor
            esperado |= {i for i in re.findall(r"`([^`]+)`", esperado_cel)}
            with self.subTest(caminhos=caminhos):
                obtido = _ids(_plano(caminhos)) - SEMPRE
                self.assertEqual(obtido, esperado, f"{' + '.join(caminhos)} ({porque})")

    def test_todo_plano_tem_os_dois_de_sempre(self):
        for caminhos in ([], ["docs/development.md"], ["apps/inbox/src/Buteco.Inbox/Program.cs"], ["global.json"]):
            with self.subTest(caminhos=caminhos):
                self.assertTrue(SEMPRE <= _ids(_plano(caminhos)))

    def test_o_related_leva_os_arquivos_da_folha(self):
        folha = ["apps/frontend/src/utils/panelDates.ts", "apps/frontend/src/features/agents/components/AgentTable.tsx"]
        passo = next(p for p in _plano(folha).passos if p.id == "fe-related")
        self.assertEqual(passo.comando[:4], ["npx", "vitest", "related", "--run"])
        self.assertEqual(sorted(passo.comando[4:]), sorted(c.removeprefix("apps/frontend/") for c in folha))

    def test_as_varreduras_sao_as_de_hoje(self):
        # Escrito à parte: um teste de varredura novo muda esta lista à mão (e o G4 o acha).
        passo = next(p for p in _plano(["apps/frontend/src/utils/panelDates.ts"]).passos if p.id == "fe-varreduras")
        self.assertEqual(passo.comando[3:], [
            "src/app/forbiddenPages.test.tsx",
            "src/components/data/surfaceTokens.test.ts",
            "src/components/forms/passwordFieldGuard.test.ts",
        ])

    def test_a_ordem_e_fixa(self):
        plano = _plano(["apps/frontend/deploy/nginx.conf", "apps/workers/src/Buteco.Workers/Program.cs",
                        "apps/frontend/src/utils/panelDates.ts"])
        self.assertEqual([p.id for p in plano.passos], [
            "check-docs", "scripts-testes", "fe-tsc", "fe-lint",
            "Buteco.Api.Tests[Nginx]", "Buteco.Connectors.Tests[Nginx]", "Buteco.Inbox.Tests[Nginx]",
            "fe-nginx", "fe-varreduras", "fe-related",
            "Buteco.Workers.Tests", "CrossAppTaskStoreCompatibility.Tests", "InboxOrchestratorRoundTrip.Tests",
        ])


PRODUCAO = re.compile(r"^(apps/[^/]+/src/|apps/frontend/deploy/)")
TESTE = re.compile(r"(\.test\.tsx?$|/src/test/)")


def _diff_de_producao(commit: str) -> list[str]:
    feito = subprocess.run(["git", "-C", str(REPO), "diff-tree", "--no-commit-id", "--name-only", "-r", commit],
                           capture_output=True, text=True)
    if feito.returncode != 0:
        raise AssertionError(f"o commit {commit} da amostra não está neste clone (clone raso?): {feito.stderr}")
    return [c for c in feito.stdout.split() if PRODUCAO.match(c) and not TESTE.search(c)]


class G2FixesDaAmostra(unittest.TestCase):
    def test_cada_fix_seleciona_o_teste_que_o_pegou(self):
        _, linhas = _tabela(AQUI / "fixes-da-amostra.md")
        self.assertEqual(len(linhas), 13)
        for commit_cel, _app, _quem, passo_cel, classe_cel in linhas:
            commit = commit_cel.strip("`")
            passo_id = passo_cel.strip("`")
            classe = classe_cel.strip("`")
            with self.subTest(commit=commit):
                caminhos = _diff_de_producao(commit)
                self.assertTrue(caminhos, f"{commit}: diff de produção vazio")
                plano = _plano(caminhos)
                passo = next((p for p in plano.passos if p.id == passo_id), None)
                self.assertIsNotNone(passo, f"{commit}: {passo_id} fora do plano {sorted(_ids(plano))}")
                if "--filter" in passo.comando:
                    filtro = passo.comando[passo.comando.index("--filter") + 1]
                    self.assertIn(filtro.split("~", 1)[1], classe, f"{commit}: o filtro {filtro} não alcança {classe}")
                if passo_id == "fe-related":
                    for caminho in caminhos:
                        self.assertIn(caminho.removeprefix("apps/frontend/"), passo.comando, commit)


SDK_DE_TESTE = re.compile(r'<PackageReference\s+Include="Microsoft\.NET\.Test\.Sdk"')


class G3AlcanceDosProjetos(unittest.TestCase):
    """A descoberta aqui é por conteúdo e independente da do script; a seleção é a do script."""

    def _projetos_de_teste(self):
        return sorted(c for c in ARQUIVOS if c.endswith(".csproj") and (REPO / c).is_file()
                      and SDK_DE_TESTE.search((REPO / c).read_text(encoding="utf-8")))

    def test_ha_projetos_de_teste(self):
        self.assertGreaterEqual(len(self._projetos_de_teste()), 8)

    def test_cada_projeto_esta_em_tudo_e_e_alcancado_pelo_proprio_diretorio(self):
        tudo = _ids(_plano(["global.json"]))
        for csproj in self._projetos_de_teste():
            nome = Path(csproj).stem
            with self.subTest(projeto=csproj):
                self.assertIn(nome, tudo, f"{csproj} fora de 'tudo'")
                dentro = str(Path(csproj).parent / "QualquerArquivo.cs")
                self.assertIn(nome, _ids(_plano([dentro])), f"tocar {dentro} não seleciona {csproj}")

    def test_os_testes_do_script_e_o_frontend_estao_em_tudo(self):
        self.assertTrue({"scripts-testes", "fe-tsc", "fe-lint", "fe-completa"} <= _ids(_plano(["global.json"])))


# G4: quem lê arquivo de fora do próprio projeto.
LITERAL_DE_CAMINHO = re.compile(
    r'(?<![$@])"((?:apps|tests|libs|deploy|docs|scripts)/[^"{}\s]+\.\w+|'
    r'Directory\.(?:Build|Packages)\.props|global\.json|package\.json|docker-compose[^"]*)"')
SOBE_PASTA = re.compile(r"new DirectoryInfo\([^)]*\);[^;]*\.Parent", re.S)
LEITURA_TS = re.compile(r"\b(readFileSync|readdirSync)\b")

# Escrito à parte: testes do frontend que leem o próprio componente, que também importam (o related os alcança).
LEEM_O_PROPRIO_COMPONENTE = {
    "apps/frontend/src/features/insights/components/AgentInsightsTab.test.tsx",
    "apps/frontend/src/features/insights/components/DeclaredGap.test.tsx",
}


class G4LeituraCruzada(unittest.TestCase):
    def _fontes_de_teste_dotnet(self):
        diretorios = {str(Path(c).parent) for c in ARQUIVOS if c.endswith(".csproj")
                      and SDK_DE_TESTE.search((REPO / c).read_text(encoding="utf-8"))} | {"tests/Shared"}
        return [c for c in ARQUIVOS if c.endswith(".cs") and (REPO / c).is_file()
                and any(c.startswith(d + "/") for d in diretorios)]

    def achados(self):
        achados = set()
        for caminho in self._fontes_de_teste_dotnet():
            texto = (REPO / caminho).read_text(encoding="utf-8")
            if LITERAL_DE_CAMINHO.search(texto) or SOBE_PASTA.search(texto):
                achados.add(caminho)
        for caminho in ARQUIVOS:
            if caminho.startswith("apps/frontend/src/") and re.search(r"\.test\.tsx?$", caminho):
                if (REPO / caminho).is_file() and LEITURA_TS.search((REPO / caminho).read_text(encoding="utf-8")):
                    achados.add(caminho)
        return achados

    def test_a_varredura_acha_os_leitores_conhecidos(self):
        # Se a heurística parar de achar estes, ela quebrou, e o guarda passaria calado.
        achados = self.achados()
        for conhecido in ("tests/Shared/NginxRouting.cs",
                          "apps/api/tests/Buteco.Api.Tests/ProductVersionCoherenceTests.cs",
                          "apps/frontend/src/app/nginxHeaders.test.ts",
                          "apps/frontend/src/components/data/surfaceTokens.test.ts"):
            self.assertIn(conhecido, achados)

    def test_toda_leitura_cruzada_tem_regra(self):
        varreduras = set(sel.varreduras_do_frontend(REPO, ARQUIVOS))
        cobertos = set(sel.LEITURAS_CRUZADAS) | varreduras | LEEM_O_PROPRIO_COMPONENTE
        sem_regra = sorted(self.achados() - cobertos)
        self.assertEqual(sem_regra, [], "leitura de arquivo de fora do projeto sem regra na seleção (design, D4): "
                                        + ", ".join(sem_regra))


class Determinismo(unittest.TestCase):
    def test_o_mesmo_diff_da_o_mesmo_plano(self):
        caminhos = "\n".join(["apps/frontend/deploy/nginx.conf", "apps/workers/src/Buteco.Workers/Program.cs",
                              "apps/frontend/src/utils/panelDates.ts", "docs/development.md"])
        saidas = [subprocess.run([sys.executable, str(SCRIPT), "--caminhos", "-", "--passos"], input=caminhos,
                                 capture_output=True, text=True, check=True).stdout for _ in range(2)]
        self.assertEqual(saidas[0], saidas[1])
        self.assertTrue(saidas[0])


if __name__ == "__main__":
    unittest.main()
