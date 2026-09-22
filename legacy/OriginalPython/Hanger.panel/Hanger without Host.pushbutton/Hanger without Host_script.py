# -*- coding: utf-8 -*-
__title__ = "Hanger without Host"

__doc__ = """How to use:

- Run the command.
- If hangers without host are found in the current view, they will be automatically isolated.
"""
__author__ = "Bruno Dias"
__min_revit_ver__ = 2023
__max_revit_ver__ = 2026

from Autodesk.Revit.DB import *
from pyrevit import revit, forms, script

doc = __revit__.ActiveUIDocument.Document
uidoc = __revit__.ActiveUIDocument
active_view = doc.ActiveView


import clr
clr.AddReference('System')
from System.Collections.Generic import List

# Context variables
doc = revit.doc
active_view = revit.active_view

# 1. Collectors for all Hangers
all_hangers = FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_FabricationHangers).WhereElementIsNotElementType().ToElements()
view_hangers = FilteredElementCollector(doc, active_view.Id).OfCategory(BuiltInCategory.OST_FabricationHangers).WhereElementIsNotElementType().ToElements()

# 2. Lists to store elements without Host
hangers_without_host_project = []
hangers_without_host_view = []

# 3. Validation loop for Project
for h in all_hangers:
    hosted_info = h.GetHostedInfo()
    if not hosted_info or hosted_info.HostId == ElementId.InvalidElementId:
        hangers_without_host_project.append(h)

# 4. Validation loop for Active View
for h in view_hangers:
    hosted_info = h.GetHostedInfo()
    if not hosted_info or hosted_info.HostId == ElementId.InvalidElementId:
        hangers_without_host_view.append(h)

# 5. Counts
count_project = len(hangers_without_host_project)
count_view = len(hangers_without_host_view)

# 6. Execution and Isolate
if count_project == 0:
    forms.alert("All Hangers have a valid Host.", title="Success")
else:
    if count_view > 0:
        # Converting IDs for the API method
        ids_to_isolate = List[ElementId]()
        for h in hangers_without_host_view:
            ids_to_isolate.Add(h.Id)

        # Transaction exactly as you prefer
        t = Transaction(doc, "Isolate Hangers without Host")
        t.Start()
        
        try:
            active_view.IsolateElementsTemporary(ids_to_isolate)
            t.Commit()
        except Exception as e:
            t.RollBack()
            print("Error isolating elements: {}".format(e))
        
        msg = "{} Hangers without Host found and isolated in this view.\n(Total in project: {})".format(count_view, count_project)
        forms.alert(msg, title="Hangers without Host")
    else:
        forms.alert("No Hangers without Host in this view, but there are {} in the entire project.".format(count_project), title="View Clean")

# # Output Log for reference
# if hangers_without_host_view:
#     output = script.get_output()
#     print("LIST OF HANGERS WITHOUT HOST IN ACTIVE VIEW:")
#     for h in hangers_without_host_view:
#         print("- ID: {} | Name: {}".format(h.Id, h.Name))