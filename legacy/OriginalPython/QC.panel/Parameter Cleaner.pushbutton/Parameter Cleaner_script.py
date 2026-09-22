# -*- coding: utf-8 -*-
__title__ = "Parameter Cleaner"
__doc__ = """How to use:

- Select the elements you want to reset.
- Run the command.
- All visible shared and project text parameters are cleared.
"""

__author__ = "Pedro Oliveira"
__min_revit_ver__ = 2023
__max_revit_ver__ = 2026


from Autodesk.Revit.DB import *
from pyrevit import revit, forms

doc = __revit__.ActiveUIDocument.Document
uidoc = __revit__.ActiveUIDocument

# Únicos built-in que a ferramenta limpa. O resto fica intacto: muitos vêm
# preenchidos pelo Revit na criação e variam por disciplina/categoria.
CLEARABLE_BUILTINS = (
    BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,
    BuiltInParameter.ALL_MODEL_MARK,
)


def should_clear(p):
    """True para texto de instância visível: shared, de projeto, Mark e Comments."""
    if p.StorageType != StorageType.String or p.IsReadOnly or not p.AsString():
        return False

    try:
        if not p.Definition.Visible:
            return False
        bip = p.Definition.BuiltInParameter
    except Exception:
        return False  # operação destrutiva: na dúvida, não mexe

    # INVALID = shared ou parâmetro de projeto
    return bip == BuiltInParameter.INVALID or bip in CLEARABLE_BUILTINS


# 1. Seleção
selection = revit.get_selection()

if not selection:
    forms.alert("No elements selected. Please select the elements to clean.",
                title="Empty Selection", exitscript=True)

# 2. Limpeza
count = 0

with revit.Transaction("Clean instance parameters"):
    for elem in selection:
        for p in elem.Parameters:
            try:
                if should_clear(p):
                    p.Set("")
                    count += 1
            except Exception:
                pass

# # 3. Resultado
# if count:
#     forms.alert("{0} parameter(s) cleared on {1} element(s).".format(count, len(selection)),title="Success")
# else:
#     forms.alert("No parameters to clean in the selection.",title="Nothing to Clean", warn_icon=True)