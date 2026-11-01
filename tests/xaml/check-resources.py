#!/usr/bin/env python3
"""Static check of XAML resource references.

WPF resolves {StaticResource} / {DynamicResource} keys at run time: a typo
compiles fine and only fails (StaticResource: crash; DynamicResource: silent
fallback) when the window opens. This walks every .xaml file of the app and
reports any key that isn't defined in the application dictionaries, in the
file's own resources, or set from code (ThemeService).

    python3 tests/xaml/check-resources.py
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Navigatueur.App"
APP_DICTIONARIES = [ROOT / "Resources" / "Theme.xaml", ROOT / "Resources" / "Icons.xaml"]

# Keys written from C# rather than declared in XAML.
CODE_DEFINED = set()

KEY_DEF = re.compile(r'x:Key="([^"{][^"]*)"')
REFERENCE = re.compile(r"\{(StaticResource|DynamicResource)\s+([^}\s]+)\s*\}")


def code_defined_keys():
    keys = set(CODE_DEFINED)
    theme_service = (ROOT / "Services" / "ThemeService.cs").read_text(encoding="utf-8")
    keys.update(re.findall(r'yield return \("(\w+)"', theme_service))
    return keys


def main():
    global_keys = code_defined_keys()
    for dictionary in APP_DICTIONARIES:
        global_keys.update(KEY_DEF.findall(dictionary.read_text(encoding="utf-8")))

    problems = []
    checked = 0
    for xaml in sorted(ROOT.rglob("*.xaml")):
        if "bin" in xaml.parts or "obj" in xaml.parts:
            continue
        text = xaml.read_text(encoding="utf-8")
        local_keys = set(KEY_DEF.findall(text))
        for line_number, line in enumerate(text.splitlines(), 1):
            for kind, key in REFERENCE.findall(line):
                if key.startswith("{"):
                    continue  # {x:Type Button}
                checked += 1
                if key not in global_keys and key not in local_keys:
                    problems.append(f"{xaml.relative_to(ROOT)}:{line_number}: {kind} '{key}' is not defined")

    # StaticResource lookups inside Theme.xaml happen while it loads: they may
    # only use keys declared earlier in Theme.xaml itself or in Icons.xaml
    # (merged first by App.xaml).
    theme = (ROOT / "Resources" / "Theme.xaml").read_text(encoding="utf-8")
    icons_keys = set(KEY_DEF.findall((ROOT / "Resources" / "Icons.xaml").read_text(encoding="utf-8")))
    declared = set()
    for line_number, line in enumerate(theme.splitlines(), 1):
        for kind, key in REFERENCE.findall(line):
            if kind == "StaticResource" and not key.startswith("{") and key not in declared and key not in icons_keys:
                problems.append(f"Resources/Theme.xaml:{line_number}: StaticResource '{key}' used before it is declared")
        declared.update(KEY_DEF.findall(line))

    for problem in problems:
        print(problem)
    print(f"{checked} resource references checked, {len(problems)} problem(s).")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
