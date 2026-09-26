#!/usr/bin/env python3
"""Check the repo-wide brace-newline rule for C# sources.

Rule: after a closing curly brace of a block, the next code must start on a new
line, unless it is another closing brace (nested block ends may stay adjacent).
Punctuation followers (`;`, `)`, `,`, `.`) remain on the brace's line, so
statements like `};` or `}).Invoke()` are fine; what is rejected is code such as
`} return null;` or `} while (x);`.

Additionally, a closing brace that ends its line (a lone `}` line) must be
separated from a following statement by one blank line. Nested block ends
(`}` before `}`), punctuation followers, and clause continuations (`else`,
`catch`, `finally`, `while`, `when`) stay adjacent; everything else — including
comments, directives, accessors, and case labels — needs the blank line.

This class has no .editorconfig / dotnet-format option (unlike `} else`,
which csharp_new_line_before_else already covers), so it is enforced by this
scanner instead. String literals, char literals, and comments are skipped.

Usage: python3 scripts/check_brace_newline.py [paths...]   (default: src)
Exits 1 and prints file:line:column for each violation.
"""

import re
import sys
from pathlib import Path

SKIP_DIRS = {"obj", "bin", ".git", "node_modules", "packages"}
# Vendored upstream source keeps upstream formatting.
SKIP_PATHS = {Path(__file__).resolve().parent.parent / "src" / "Veldrid"}

# Keywords (or a colon, for nested sub-patterns) that introduce a property
# pattern: a brace pair opened after one of these is a pattern, not a block,
# so a designation identifier may follow its closing brace on the same line.
PATTERN_INTRODUCERS = {"is", "not", "and", "or", "when", ":"}

# Keywords that legitimately continue the enclosing statement on the line
# after a closing brace and therefore do not need a blank line separator.
CLAUSE_KEYWORDS = {"else", "catch", "finally", "while", "when"}


def opens_property_pattern(line, close_idx):
    """Whether the brace closing at close_idx belongs to a property pattern.

    Finds the matching `{` by a same-line backward scan and checks what
    introduced it. Multi-line patterns are not matched (none in this repo);
    a block whose `{` sits on an earlier line fails the scan and is treated
    as a block, which is the correct verdict for try/catch and do/while.
    """
    depth = 0
    k = close_idx
    while k >= 0:
        if line[k] == "}":
            depth += 1
        elif line[k] == "{":
            depth -= 1
            if depth == 0:
                match = re.search(r"([A-Za-z_]+|:)\s*$", line[:k])
                return match is not None and match.group(1) in PATTERN_INTRODUCERS
        k -= 1
    return False


def violations(text, path):
    result = []
    i, line, col = 0, 1, 1

    def advance(count=1):
        nonlocal i, line, col
        for _ in range(count):
            if i < len(text) and text[i] == "\n":
                line += 1
                col = 1
            else:
                col += 1
            i += 1

    n = len(text)
    while i < n:
        c = text[i]

        if c == "\n":
            advance()
            continue

        # Comments.
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                advance()
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            advance(2)
            while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
                advance()
            advance(2)
            continue

        # Raw strings (""" or """), possibly $/@-prefixed: the prefix char was
        # consumed below as plain code, the quotes start the literal.
        if c == '"' and text[i : i + 3] == '"""':
            fence = '"""'
            while text[i : i + len(fence) + 1] == '"' + fence:
                fence += '"'
            advance(len(fence))
            while i < n and text[i : i + len(fence)] != fence:
                advance()
            advance(len(fence))
            continue

        # Regular and verbatim strings. Prefixes ($, @) are just plain chars here.
        if c == '"':
            verbatim = i > 0 and text[i - 1] == "@" and not (i > 1 and text[i - 2] == "$")
            advance()
            while i < n:
                if verbatim:
                    if text[i] == '"' and i + 1 < n and text[i + 1] == '"':
                        advance(2)
                        continue
                    if text[i] == '"':
                        advance()
                        break
                else:
                    if text[i] == "\\":
                        advance(2)
                        continue
                    if text[i] == '"':
                        advance()
                        break
                advance()
            continue

        # Char literal.
        if c == "'":
            advance()
            while i < n:
                if text[i] == "\\":
                    advance(2)
                    continue
                if text[i] == "'":
                    advance()
                    break
                advance()
            continue

        # The rule itself: closing brace followed by horizontal whitespace and
        # then an identifier-start character. Two exemptions: empty braces
        # ({ } is a null-check property pattern, and an empty block followed
        # by code on the same line is not legal C#), and property patterns
        # whose designation identifier legitimately trails the closing brace.
        if c == "}":
            j = i + 1
            while j < n and text[j] in " \t":
                j += 1
            if j < n and (text[j].isalpha() or text[j] == "_"):
                k = i - 1
                while k >= 0 and text[k] in " \t":
                    k -= 1
                line_start = text.rfind("\n", 0, i) + 1
                line_end = text.find("\n", i)
                line_slice = text[line_start:line_end if line_end != -1 else n]
                if text[k] != "{" and not opens_property_pattern(line_slice, i - line_start):
                    result.append(f"{path}:{line}:{col + 1}: code after closing brace")
            elif j >= n or text[j] == "\n":
                # Lone closing-brace line: the next line must be blank, another
                # closing brace, a punctuation follower, or a clause keyword.
                # "Lone" requires only whitespace before the brace on its line,
                # so one-liners like `{ get; set; }` are not covered here.
                line_start = text.rfind("\n", 0, i) + 1
                if text[line_start:i].strip():
                    advance()
                    continue
                k = (j + 1) if j < n else n
                line_end2 = text.find("\n", k) if k < n else -1
                if line_end2 == -1:
                    line_end2 = n
                follower = text[k:line_end2].strip()
                if follower and follower[0] not in ";),.}":
                    match = re.match(r"[A-Za-z_]+", follower)
                    if not match or match.group(0) not in CLAUSE_KEYWORDS:
                        result.append(
                            f"{path}:{line}:{col + 1}: statement follows closing brace "
                            f"without a blank line")
            advance()
            continue

        advance()

    return result


def main():
    roots = sys.argv[1:] or ["src"]
    files = []
    for root in roots:
        path = Path(root)
        if path.is_dir():
            files.extend(
                p for p in path.rglob("*.cs")
                if not any(part in SKIP_DIRS for part in p.parts)
                and not any(p.resolve().is_relative_to(skip) for skip in SKIP_PATHS)
            )
        elif path.suffix == ".cs":
            files.append(path)

    failed = False
    for path in sorted(files):
        for message in violations(path.read_text(encoding="utf-8"), path):
            print(message)
            failed = True

    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
