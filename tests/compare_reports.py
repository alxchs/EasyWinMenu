"""Compara dois relatorios do GroupProbe.

Uso:
    python tests/compare_reports.py tests/baseline/report-master.json <outro relatorio>

Ignora so o que e identidade da execucao (`label`, `exeFileVersion`, `fixture`). Todo o
resto — geometria das janelas, estilos estendidos, hash das capturas, o menu de contexto
inteiro e a configuracao gravada — tem que bater exatamente. Sai com codigo 1 se algo
divergir, para poder ser usado como portao em script.
"""
import json
import sys

IGNORE_TOP = {"label", "exeFileVersion", "fixture"}


def walk(node, path=""):
    """Achata o JSON em pares caminho -> valor, para o diff apontar o campo exato."""
    if isinstance(node, dict):
        for key, value in node.items():
            yield from walk(value, f"{path}.{key}" if path else key)
    elif isinstance(node, list):
        for index, value in enumerate(node):
            yield from walk(value, f"{path}[{index}]")
    else:
        yield path, node


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 2

    left_path, right_path = sys.argv[1], sys.argv[2]
    left = json.load(open(left_path, encoding="utf-8"))
    right = json.load(open(right_path, encoding="utf-8"))

    for key in IGNORE_TOP:
        left.pop(key, None)
        right.pop(key, None)

    left_flat = dict(walk(left))
    right_flat = dict(walk(right))

    only_left = sorted(set(left_flat) - set(right_flat))
    only_right = sorted(set(right_flat) - set(left_flat))
    changed = sorted(k for k in set(left_flat) & set(right_flat) if left_flat[k] != right_flat[k])

    print(f"referencia : {left_path}  ({len(left_flat)} campos)")
    print(f"comparado  : {right_path}  ({len(right_flat)} campos)")

    if not (only_left or only_right or changed):
        print("\nIDENTICO: nenhum campo divergiu.")
        return 0

    if only_left:
        print(f"\nSO NA REFERENCIA ({len(only_left)}):")
        for key in only_left:
            print(f"  - {key} = {left_flat[key]!r}")
    if only_right:
        print(f"\nSO NO COMPARADO ({len(only_right)}):")
        for key in only_right:
            print(f"  + {key} = {right_flat[key]!r}")
    if changed:
        print(f"\nDIVERGENTES ({len(changed)}):")
        for key in changed:
            print(f"  ~ {key}\n      referencia: {left_flat[key]!r}\n      comparado : {right_flat[key]!r}")

    return 1


if __name__ == "__main__":
    sys.exit(main())
