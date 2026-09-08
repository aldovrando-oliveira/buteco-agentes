#!/usr/bin/env python3
"""Checagem de integridade da documentação do repositório.

Rode a partir de qualquer diretório:

    python3 scripts/check-docs.py

Sai com código 0 se tudo estiver íntegro, 1 se houver violação. Cada violação
é impressa como `arquivo:linha: mensagem`.

Por que este script existe: a documentação deste repositório já divergiu do
código em silêncio — links apontando para mudanças que o arquivamento moveu,
contagem de apps desatualizada, escopo declarado como pendente muito depois de
entregue. A convenção 8 da casa diz que registro que pode ficar incompleto em
silêncio pede checagem de integridade, não documentação em prosa. Isto é a
aplicação dessa regra à própria documentação.

O que NÃO é verificado, deliberadamente: afirmação semanticamente obsoleta.
Nenhum script detecta que "ainda não consome o backend" virou mentira. Por
isso a documentação em docs/ descreve estado atual em vez de escopo e planos,
que é a categoria de frase que envelhece mal.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path
from urllib.parse import unquote, urlparse

REPO = Path(__file__).resolve().parent.parent

# Arquivos de trabalho do mantenedor. São fonte para docs/, nunca destino, e
# não podem ser corrigidos pela mudança que introduziu este script — varrê-los
# só produziria ruído que ninguém tem permissão de silenciar.
PROTECTED = {"01-ARQUITETURA_E_CONVENCOES.md", "02-HISTORICO_E_STATUS.md"}

# Diretórios que não contêm documentação do projeto.
SKIP_DIRS = {".git", "node_modules", "bin", "obj", "dist", ".vs", ".idea", "TestResults"}

# Mudanças arquivadas são registro histórico congelado: descrevem o que foi
# decidido e escrito naquele momento. Corrigir um link dentro de uma delas
# falsificaria o registro, que é a mesma objeção que protege os dois arquivos
# em PROTECTED.
#
# Elas carregam o defeito em massa, e pela mesma causa mecânica que este
# script existe para pegar: um link escrito como ../../../apps/... apontava
# para a raiz enquanto a mudança vivia em openspec/changes/<nome>/, e o
# arquivamento a moveu um nível mais fundo, para
# openspec/changes/archive/AAAA-MM-DD-<nome>/, fazendo todo caminho relativo
# cair em openspec/. Os alvos existem; a profundidade é que mudou.
#
# O valor desta checagem está na documentação viva — o que um leitor de hoje
# alcança. Varrer o arquivo morto só produz ruído irreparável que treinaria
# qualquer pessoa a ignorar a saída inteira.
SKIP_PATHS = {REPO / "openspec" / "changes" / "archive"}

ARCHIVE_DIR = REPO / "openspec" / "changes" / "archive"
APPS_DIR = REPO / "apps"
ARCHITECTURE_DOC = REPO / "docs" / "architecture.md"
CHANGELOG = REPO / "CHANGELOG.md"

# [texto](alvo) — captura o alvo, ignorando imagens ![...](...) por não haver
# nenhuma hoje e por elas seguirem a mesma regra se aparecerem.
MD_LINK = re.compile(r"\[[^\]]*\]\(([^)]+)\)")

# Referência a mudança OpenSpec fora de archive/, em link ou em texto corrido.
OPENSPEC_REF = re.compile(r"openspec/changes/(?!archive/)([a-z0-9][a-z0-9-]*)")


class Violations:
    def __init__(self) -> None:
        self.items: list[str] = []

    def add(self, path: Path, line: int | None, message: str) -> None:
        rel = path.relative_to(REPO)
        where = f"{rel}:{line}" if line else str(rel)
        self.items.append(f"{where}: {message}")

    def __bool__(self) -> bool:
        return bool(self.items)


def markdown_files() -> list[Path]:
    """Documentação viva do repositório.

    Exclui diretórios de build, os arquivos protegidos do mantenedor e as
    mudanças arquivadas — ver PROTECTED e SKIP_PATHS para o porquê de cada
    exclusão.
    """
    found = []
    for path in REPO.rglob("*.md"):
        if any(part in SKIP_DIRS for part in path.parts):
            continue
        if path.name in PROTECTED and path.parent == REPO:
            continue
        if any(skip in path.parents for skip in SKIP_PATHS):
            continue
        found.append(path)
    return sorted(found)


def archived_changes() -> dict[str, str]:
    """Mapeia nome da mudança -> nome do diretório com prefixo de data."""
    if not ARCHIVE_DIR.is_dir():
        return {}
    mapping = {}
    for entry in ARCHIVE_DIR.iterdir():
        if not entry.is_dir():
            continue
        # 2026-08-11-inbox-adapter-waha -> inbox-adapter-waha
        stripped = re.sub(r"^\d{4}-\d{2}-\d{2}-", "", entry.name)
        mapping[stripped] = entry.name
    return mapping


def check_relative_links(violations: Violations) -> None:
    """Todo link relativo em .md deve resolver para um caminho existente."""
    for path in markdown_files():
        for lineno, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            for target in MD_LINK.findall(line):
                target = target.strip()
                if not target:
                    continue
                # Ignora URL absoluta, âncora pura, mailto e template de link
                # de referência não resolvido.
                parsed = urlparse(target)
                if parsed.scheme or target.startswith("#") or target.startswith("<"):
                    continue
                # Remove âncora e query do alvo antes de resolver o caminho.
                bare = unquote(parsed.path)
                if not bare:
                    continue
                resolved = (path.parent / bare).resolve()
                if not resolved.exists():
                    violations.add(
                        path, lineno, f"link relativo não resolve: {target}"
                    )


def check_archived_change_links(violations: Violations) -> None:
    """Referência a mudança arquivada deve usar o caminho de archive/.

    Esta é a causa mecânica mais comum de link quebrado no repositório: o link
    é escrito durante a mudança, apontando para openspec/changes/<nome>/, e o
    arquivamento move a pasta para openspec/changes/archive/AAAA-MM-DD-<nome>/.
    """
    archive = archived_changes()
    if not archive:
        return
    for path in markdown_files():
        for lineno, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            for name in OPENSPEC_REF.findall(line):
                if name in archive:
                    violations.add(
                        path,
                        lineno,
                        f"mudança '{name}' está arquivada; use "
                        f"openspec/changes/archive/{archive[name]}/",
                    )


def check_apps_documented(violations: Violations) -> None:
    """Todo app em apps/ aparece na doc de arquitetura, e vice-versa.

    A checagem vale nos dois sentidos: app declarado sem contraparte real é
    tão problema quanto app real sem documentação.
    """
    if not ARCHITECTURE_DOC.exists():
        violations.add(ARCHITECTURE_DOC, None, "documento de arquitetura não existe")
        return
    if not APPS_DIR.is_dir():
        return

    real = {p.name for p in APPS_DIR.iterdir() if p.is_dir() and p.name not in SKIP_DIRS}
    text = ARCHITECTURE_DOC.read_text(encoding="utf-8")
    documented = set(re.findall(r"apps/([a-z0-9][a-z0-9-]*)", text))

    for name in sorted(real - documented):
        violations.add(
            ARCHITECTURE_DOC, None, f"app 'apps/{name}' existe mas não está documentado"
        )
    for name in sorted(documented - real):
        violations.add(
            ARCHITECTURE_DOC,
            None,
            f"app 'apps/{name}' está documentado mas não existe em apps/",
        )


def check_changelog_unreleased(violations: Violations) -> None:
    """CHANGELOG.md precisa manter a seção [Unreleased]."""
    if not CHANGELOG.exists():
        violations.add(CHANGELOG, None, "CHANGELOG.md não existe")
        return
    text = CHANGELOG.read_text(encoding="utf-8")
    if not re.search(r"^## \[Unreleased\]", text, re.MULTILINE):
        violations.add(CHANGELOG, None, "seção '## [Unreleased]' ausente")


def main() -> int:
    violations = Violations()

    check_relative_links(violations)
    check_archived_change_links(violations)
    check_apps_documented(violations)
    check_changelog_unreleased(violations)

    if violations:
        print(f"Integridade da documentação: {len(violations.items)} violação(ões)\n")
        for item in violations.items:
            print(f"  {item}")
        print(
            "\nVer docs/conventions.md, seção 'Documentação', para as regras "
            "que sustentam cada checagem."
        )
        return 1

    print("Integridade da documentação: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
