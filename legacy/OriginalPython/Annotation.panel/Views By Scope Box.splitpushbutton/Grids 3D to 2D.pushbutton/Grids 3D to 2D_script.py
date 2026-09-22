# -*- coding: utf-8 -*-
__title__ = "Grids 3D to 2D"
__author__ = "Bruno Dias"
__doc__ = """How to use:

- Click the button to convert all visible Grids from 3D to 2D extents."""

__min_revit_ver__ = 2023
__max_revit_ver__ = 2026

from Autodesk.Revit.DB import *
from pyrevit import revit, forms

doc = revit.doc
active_view = doc.ActiveView

# 1. Coletar Grids visíveis
grids = FilteredElementCollector(doc, active_view.Id).OfClass(Grid).ToElements()

if not grids:
    forms.alert("No Grids found in this view.", title="Warning")
else:
    t = Transaction(doc, "Grids 3D to 2D")
    t.Start()
    
    count = 0
    for g in grids:
        try:
            # End0 = Início, End1 = Fim
            g.SetDatumExtentType(DatumEnds.End0, active_view, DatumExtentType.ViewSpecific)
            g.SetDatumExtentType(DatumEnds.End1, active_view, DatumExtentType.ViewSpecific)
            count += 1
        except:
            continue
            
    t.Commit()
    revit.uidoc.RefreshActiveView()
    
