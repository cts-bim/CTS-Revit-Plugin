# -*- coding: utf-8 -*-
__title__ = "Align By Z"
__doc__ = """How to use:

- Select ONE element and run the command.
- Pick the reference element.
- The element will be aligned to the reference along MODEL Z axis.
"""

__author__ = "Bruno Dias"
__min_revit_ver__ = 2023
__max_revit_ver__ = 2026

from Autodesk.Revit.DB import *
from pyrevit import revit, forms, script

doc = __revit__.ActiveUIDocument.Document
uidoc = __revit__.ActiveUIDocument
active_view = doc.ActiveView


def alert(msg, title="Info"):
    forms.alert(msg, title=title)


# ---------------------------------------------------------
#   UTILITÁRIOS DE CONECTORES
# ---------------------------------------------------------

def is_physical_connector(conn):
    """Retorna True somente para conectores físicos relevantes (End ou Curve)."""
    try:
        return conn.ConnectorType in (ConnectorType.End, ConnectorType.Curve)
    except:
        return False


def get_connectors(elem):
    """Retorna apenas conectores físicos (descarta insulation, logical, analytical)."""
    raw = []
    try:
        if hasattr(elem, "MEPModel") and elem.MEPModel:
            raw = list(elem.MEPModel.ConnectorManager.Connectors)
    except:
        pass

    if not raw:
        try:
            raw = list(elem.ConnectorManager.Connectors)
        except:
            pass

    return [c for c in raw if is_physical_connector(c)]


def get_connector_points(elem):
    conns = get_connectors(elem)
    return [c.Origin for c in conns]


def get_free_connector_points(elem):
    conns = get_connectors(elem)
    free = []
    for c in conns:
        try:
            if not c.IsConnected:
                free.append(c.Origin)
        except:
            pass
    return free


def get_nearest_connector_pair(elemA, elemB):
    """Retorna o par de conectores mais próximos, priorizando conectores livres."""

    # 1) tentar conectores livres
    conA = get_free_connector_points(elemA)
    conB = get_free_connector_points(elemB)

    # 2) fallback: se não houver conectores livres
    if not conA:
        conA = get_connector_points(elemA)
    if not conB:
        conB = get_connector_points(elemB)

    if not conA or not conB:
        return None, None

    bestA = None
    bestB = None
    min_dist = float("inf")

    for a in conA:
        for b in conB:
            d = a.DistanceTo(b)
            if d < min_dist:
                min_dist = d
                bestA = a
                bestB = b

    return bestA, bestB


# ---------------------------------------------
#  MAIN
# ---------------------------------------------

if not isinstance(active_view, View3D):
    alert(
        "This tool can only be used in a 3D View.\n\nOpen a 3D view and try again.",
        "Invalid View"
    )
    script.exit()

# 1 — pegar seleção existente
selection_ids = uidoc.Selection.GetElementIds()
selected = [doc.GetElement(id) for id in selection_ids]

if not selected:
    alert("No elements selected.", "Cancelled")
    script.exit()

# exige exatamente 1 elemento selecionado
if len(selected) != 1:
    alert("Select exactly ONE element to align.", "Cancelled")
    script.exit()

el = selected[0]

# 2 — escolher elemento de referência
with forms.WarningBar(title="Pick the reference element"):
    try:
        ref_elem = revit.pick_element()
    except:
        ref_elem = None

if not ref_elem:
    alert("Reference element not selected.", "Cancelled")
    script.exit()

# 3 — obter o par de conectores mais próximos
el_point, ref_point = get_nearest_connector_pair(el, ref_elem)

if el_point is None or ref_point is None:
    alert("One of the elements has no connector.", "No connectors")
    script.exit()


# ---------------------------------------------
#  COMPUTE MOVE (Z AXIS ONLY)
# ---------------------------------------------
delta = ref_point.Z - el_point.Z
move = XYZ(0, 0, delta)

if move.IsZeroLength():
    alert("Element is already aligned on X axis.")
    script.exit()


# ---------------------------------------------
#  APPLY MOVE
# ---------------------------------------------
t = Transaction(doc, "Align by Y")
t.Start()

try:
    ElementTransformUtils.MoveElement(doc, el.Id, move)
    # alert("Moved 1 element.", "Done")
except Exception as ex:
    alert("Failed to move: {}".format(ex), "Error")

t.Commit()
