# -*- coding: utf-8 -*-
__title__ = "Smart Concat"
__doc__ = """How to use:

- Click the icon and follow the steps to concatenate parameters.
"""

__author__ = "Bruno Dias"
__min_revit_ver__ = 2023
__max_revit_ver__ = 2026


from Autodesk.Revit.DB import *
from pyrevit import revit, forms
from rpw.ui.forms import FlexForm, Label, ComboBox, TextBox, Separator, Button
import clr
import sys

doc = __revit__.ActiveUIDocument.Document
active_view = doc.ActiveView

CATEGORIES = {
    "Pipe Accessories": BuiltInCategory.OST_PipeAccessory,
    "Fabrication Hangers": BuiltInCategory.OST_FabricationHangers,
    "Pipes": BuiltInCategory.OST_PipeCurves,
    "Mechanical Equipment": BuiltInCategory.OST_MechanicalEquipment,
    "MEP Fabrication Pipework": BuiltInCategory.OST_FabricationPipework,
}

# Fabrication parts expõem ~970 parâmetros via API, mas só ~210 aparecem na
# paleta de propriedades. Deixe False para listar também os ocultos (eM_/eV_).
ONLY_VISIBLE_PARAMS = True

# ----------------------------------------------------
# FUNÇÕES DE PARÂMETROS
# ----------------------------------------------------
def is_visible(p):
    """True se o parâmetro aparece na paleta de propriedades do Revit."""
    if not ONLY_VISIBLE_PARAMS:
        return True
    try:
        return bool(p.Definition.Visible)
    except Exception:
        # Definition sem InternalDefinition: mantém, é o comportamento antigo
        return True


def sample_one_per_type(elems):
    """Um elemento por ElementType.

    O conjunto de parâmetros é igual dentro do mesmo tipo, então varrer todos os
    elementos só repete trabalho.
    """
    seen = set()
    out = []
    for e in elems:
        key = e.GetTypeId().IntegerValue
        if key not in seen:
            seen.add(key)
            out.append(e)
    return out


def get_all_parameters_from_category(bic, elems=None):
    if elems is None:
        elems = FilteredElementCollector(doc).OfCategory(bic)\
            .WhereElementIsNotElementType().ToElements()

    params = {}
    type_cache = {}

    for e in sample_one_per_type(elems):
        for p in e.Parameters:
            if p and p.Definition and is_visible(p):
                params[p.Definition.Name] = p.Id

        tid = e.GetTypeId()
        key = tid.IntegerValue
        if key not in type_cache:
            type_cache[key] = doc.GetElement(tid)

        etype = type_cache[key]
        if etype:
            for p in etype.Parameters:
                if p and p.Definition and is_visible(p):
                    params[p.Definition.Name] = p.Id

    return params


def get_editable_string_parameters(elems):
    """Parâmetros de texto graváveis — candidatos a destino da concatenação."""
    params_dict = {}
    if not elems:
        return params_dict

    for e in sample_one_per_type(elems):
        for p in e.Parameters:
            if (
                p and
                p.Definition and
                not p.IsReadOnly and
                p.StorageType == StorageType.String and
                is_visible(p)
            ):
                params_dict[p.Definition.Name] = p.Id

    return params_dict


def get_param_value(elem, param_id):
    p = next((p for p in elem.Parameters if p.Id == param_id), None)

    if not p:
        etype = doc.GetElement(elem.GetTypeId())
        if etype:
            p = next((p for p in etype.Parameters if p.Id == param_id), None)

    if not p:
        return ""

    try:
        if p.StorageType == StorageType.String:
            return p.AsString() or ""

        v = p.AsValueString()
        if v:
            return v

        if p.StorageType == StorageType.Integer:
            return str(p.AsInteger() or "")

        if p.StorageType == StorageType.Double:
            return str(p.AsDouble() or "")
    except:
        return ""

    return ""

# ----------------------------------------------------
# FORMULÁRIOS
# ----------------------------------------------------
def show_main_window():
    form = FlexForm("Concatenation Settings", [
        Label("Category:"),
        ComboBox("cat", sorted(CATEGORIES.keys())),
        Separator(),

        Label("Scope:"),
        ComboBox("scope", ["Active view", "Entire project"]),
        Separator(),

        Label("Number of parameters to concatenate (2 to 5):"),
        ComboBox("qtd", ["2","3","4","5"]),
        Separator(),

        Button("Continue", Name="ok")
    ])
    return form


def show_params_window(common_params, editable_params, qtd):
    # Reduzi o texto dos Labels para economizar espaço vertical
    items = [Label("prefixes / Select parameters / suffixes:")]

    names_common = list(common_params.keys())
    names_editable = list(editable_params.keys())

    for i in range(qtd):
        idx = i + 1

        items.append(Label("Prefix | P{} | Suffix".format(idx)))
        items.append(TextBox("pref{}".format(idx), Text=""))
        items.append(ComboBox("p{}".format(idx), names_common))
        items.append(TextBox("suf{}".format(idx), Text=""))
        items.append(Separator())

    items.append(Label("Parameter to set:"))
    items.append(ComboBox("dest", names_editable))
    items.append(Separator())

    items.append(Button("Apply"))

    # AJUSTE: Definindo altura fixa e permitindo redimensionamento para não sumir o botão
    return FlexForm("Select Parameters", items, height=700, can_resize=True)

# ----------------------------------------------------
# EXECUÇÃO
# ----------------------------------------------------
form1 = show_main_window()
form1.show()
res1 = form1.values
if not res1: sys.exit()

bic = CATEGORIES[res1["cat"]]
qtd = int(res1["qtd"])
scope = res1.get("scope", "Active view")

if scope == "Active view":
    elems = FilteredElementCollector(doc, active_view.Id).OfCategory(bic)\
        .WhereElementIsNotElementType().ToElements()
else:
    elems = FilteredElementCollector(doc).OfCategory(bic)\
        .WhereElementIsNotElementType().ToElements()

if not elems:
    forms.alert("The selected category has no elements in the chosen scope ({})".format(scope))
    sys.exit()

all_params = get_all_parameters_from_category(bic, elems=elems)
editable_params = get_editable_string_parameters(elems)
form2 = show_params_window(all_params, editable_params, qtd)
form2.show()
res2 = form2.values
if not res2: sys.exit()

selected_param_ids = []
prefixes = []
suffixes = []

for i in range(1, qtd+1):
    pname = res2["p{}".format(i)]
    selected_param_ids.append(all_params[pname])

    prefixes.append(res2["pref{}".format(i)])
    suffixes.append(res2["suf{}".format(i)])

dest_param_id = editable_params[res2["dest"]]

# ----------------------------------------------------
# TRANSAÇÃO
# ----------------------------------------------------
t = Transaction(doc, "SmartConcat")
t.Start()

for elem in elems:
    parts = []

    for i in range(qtd):
        val = get_param_value(elem, selected_param_ids[i])
        pref = prefixes[i]
        suf = suffixes[i]

        parts.append(pref + val + suf)

    final = "".join(parts)

    p = next((p for p in elem.Parameters if p.Id == dest_param_id), None)
    if not p:
        etype = doc.GetElement(elem.GetTypeId())
        if etype:
            p = next((p for p in etype.Parameters if p.Id == dest_param_id), None)

    if p and not p.IsReadOnly:
        p.Set(final)

t.Commit()

forms.alert("Concatenation completed!", title="Done")