# -*- coding: utf-8 -*-
__title__ = "Duplicate Finder"

__doc__ = """How to use:

- Open the view you want to check.
- Run the command.
- Pick the categories to analyse.
- Elements sharing Category + Family and occupying the same space are isolated in the view and listed in the output window.
"""
__author__ = "Pedro Oliveira"
__min_revit_ver__ = 2023
__max_revit_ver__ = 2026

from collections import defaultdict

from Autodesk.Revit.DB import *
from pyrevit import revit, forms, script

import clr
clr.AddReference('System')
from System.Collections.Generic import List

# Context variables
doc = revit.doc
uidoc = revit.uidoc
active_view = revit.active_view

# Categorias que interessam. O resto do modelo nem chega a ser coletado.
TARGET_CATEGORIES = (BuiltInCategory.OST_FabricationPipework,
                     BuiltInCategory.OST_FabricationHangers,
                     BuiltInCategory.OST_PipeAccessory,
                     BuiltInCategory.OST_MechanicalEquipment,
                     BuiltInCategory.OST_GenericModel)

# Tolerância de coincidência. O Revit trabalha em pés internamente.
TOLERANCE_INCHES = 1.0
TOL = TOLERANCE_INCHES / 12.0

# Teto da tolerância como fração do tamanho da peça. Ver effective_tol().
SIZE_RATIO = 0.10

# Vistas que não aceitam Temporary Isolate
BLOCKED_VIEWS = (ViewType.Schedule, ViewType.DrawingSheet, ViewType.Legend,
                 ViewType.ProjectBrowser, ViewType.SystemBrowser)


###### Functions ########

def elem_id_value(elem):
    """ElementId como número. Value é 2024+, IntegerValue é 2023."""
    try:
        return elem.Id.Value
    except AttributeError:
        return elem.Id.IntegerValue


def get_family_name(elem):
    """Nome da Family como aparece nas Properties.

    FabricationParts não expõem FamilyName utilizável no tipo (o Type é sempre
    'Default'), por isso ELEM_FAMILY_PARAM vem primeiro: é o único que responde
    igual para FamilyInstance e para peças de fabricação.
    """
    try:
        p = elem.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)
        if p:
            name = p.AsValueString()
            if name:
                return name
    except Exception:
        pass

    try:
        etype = doc.GetElement(elem.GetTypeId())
        if etype:
            return etype.FamilyName or etype.Name
    except Exception:
        pass

    return elem.Category.Name


def collect_model_elements():
    """Elementos das categorias-alvo desenhados na vista ativa.

    Famílias aninhadas ficam de fora: duplicar a mãe duplica as filhas junto,
    então elas só inflariam o resultado com o mesmo problema contado N vezes.
    SuperComponent é justamente quem aponta para a família mãe.
    """
    cat_filter = ElementMulticategoryFilter(List[BuiltInCategory](TARGET_CATEGORIES))

    collector = (FilteredElementCollector(doc, active_view.Id)
                 .WherePasses(cat_filter)
                 .WhereElementIsNotElementType())

    by_category = defaultdict(list)
    for elem in collector:
        if elem.Category is None:
            continue
        if isinstance(elem, FamilyInstance) and elem.SuperComponent is not None:
            continue
        by_category[elem.Category.Name].append(elem)

    return by_category


def get_signature(elem):
    """(Min, Max) do BoundingBox em coordenadas de modelo, ou None.

    O BoundingBox é a única assinatura que existe para todas as categorias em
    uso: Fittings e Hangers não devolvem Location, e nem todo Mechanical
    Equipment tem Connector. Como aqui se compara cópia contra cópia, os
    defeitos clássicos do BBox (não é o centroide real, gira com o elemento)
    não afetam o resultado: duas cópias idênticas dão a mesma caixa.
    """
    bbox = elem.get_BoundingBox(None)
    if bbox is None:
        return None

    tf = bbox.Transform
    return (tf.OfPoint(bbox.Min), tf.OfPoint(bbox.Max))


def max_extent(box):
    """Maior dimensão da caixa."""
    box_min, box_max = box
    return max(box_max.X - box_min.X,
               box_max.Y - box_min.Y,
               box_max.Z - box_min.Z)


def effective_tol(a, b):
    """Tolerância limitada pelo tamanho da menor das duas peças.

    1" é desprezível num tubo de 3 m e enorme num coupling de 2". Sem esse
    teto, o arranjo comum coupling-nipple-coupling entra todo no mesmo cluster,
    porque a tolerância engole a peça inteira.

    Apertar não custa nada: duplicata de verdade é copy/paste no lugar e tem
    distância zero. A tolerância só existe para modelagem desleixada, e é em
    peça grande que ela faz falta.
    """
    return min(TOL, SIZE_RATIO * min(max_extent(a), max_extent(b)))


def same_box(a, b):
    """True se os dois BoundingBox coincidem dentro da tolerância efetiva."""
    tol = effective_tol(a, b)
    a_min, a_max = a
    b_min, b_max = b
    return (abs(a_min.X - b_min.X) <= tol and
            abs(a_min.Y - b_min.Y) <= tol and
            abs(a_min.Z - b_min.Z) <= tol and
            abs(a_max.X - b_max.X) <= tol and
            abs(a_max.Y - b_max.Y) <= tol and
            abs(a_max.Z - b_max.Z) <= tol)


def find_clusters(entries):
    """Agrupa entries [(elem, sig)] que ocupam o mesmo espaço.

    Compara todos contra todos dentro do balde. O balde já é só uma Family de
    uma categoria dentro da vista ativa, então é pequeno o bastante para não
    valer a pena complicar.

    Cluster é o grupo de elementos empilhados no mesmo lugar - nem sempre são
    pares: uma rotina rodada três vezes deixa três cópias.
    """
    clusters = []
    consumed = set()

    for idx, (elem, sig) in enumerate(entries):
        if idx in consumed:
            continue

        group = [idx]
        for other in range(idx + 1, len(entries)):
            if other in consumed:
                continue
            if same_box(sig, entries[other][1]):
                group.append(other)

        if len(group) > 1:
            consumed.update(group)
            members = [entries[i][0] for i in group]
            members.sort(key=elem_id_value)  # o menor Id é o keeper
            clusters.append(members)

    return clusters


###### Main Execution ########

# 1. Validação da vista
if active_view.IsTemplate or active_view.ViewType in BLOCKED_VIEWS:
    forms.alert("Open a model view (plan, section or 3D) before running.",
                title="Unsupported View", exitscript=True)

# 2. Coleta do que está visível na vista
by_category = collect_model_elements()

if not by_category:
    forms.alert("None of the checked categories are visible in this view.",
                title="Nothing to Check", exitscript=True)

# 3. Seleção de categorias, ordenada por quantidade
cat_names = sorted(by_category.keys(), key=lambda n: -len(by_category[n]))
labels = ["{}  ({})".format(name, len(by_category[name])) for name in cat_names]
label_to_cat = dict(zip(labels, cat_names))

picked = forms.SelectFromList.show(labels,
                                   title="Select Categories to Check",
                                   multiselect=True,
                                   button_name="Check Duplicates")
if not picked:
    script.exit()

# 4. Baldes por Category + Family
buckets = defaultdict(list)

for label in picked:
    for elem in by_category[label_to_cat[label]]:
        sig = get_signature(elem)
        if sig is None:
            continue  # sem geometria não há o que medir
        buckets[(elem.Category.Name, get_family_name(elem))].append((elem, sig))

# 5. Clusterização dentro de cada balde
clusters = []
for entries in buckets.values():
    if len(entries) > 1:
        clusters.extend(find_clusters(entries))

if not clusters:
    forms.alert("No duplicated elements found in this view.",
                title="View Clean", exitscript=True)

# 6. Isolate
ids_to_isolate = List[ElementId]()
total_elements = 0

for group in clusters:
    total_elements += len(group)
    for elem in group:
        ids_to_isolate.Add(elem.Id)

t = Transaction(doc, "Isolate Duplicated Elements")
t.Start()

try:
    active_view.IsolateElementsTemporary(ids_to_isolate)
    t.Commit()
except Exception as e:
    t.RollBack()
    print("Error isolating elements: {}".format(e))

# 7. Resultado
forms.alert("{} duplicated elements found and isolated in this view.".format(total_elements),
            title="Duplicate Finder")
