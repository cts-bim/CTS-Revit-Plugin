# -*- coding: utf-8 -*-
__title__ = "Rotate 90° CW"
__author__ = "Pedro Oliveira"
__doc__ = """How to use:

- Select all desired MEP Fabrication Parts.
- Run the command.
- All selected Fabrication Parts will be rotated 90° clockwise.
- Pinned Fabrication Parts are skipped automatically.
"""

__min_revit_ver__ = 2023
__max_revit_ver__ = 2026

import math
from pyrevit import revit, forms
from Autodesk.Revit.DB import (
    ElementTransformUtils, Line, XYZ,
    FabricationPart, LocationPoint
)

doc = revit.doc
selection = revit.get_selection()

# Ensure there is an active selection before proceeding
if not selection:
    forms.alert("No elements selected. Please select MEP Fabrication Parts first.", title="Empty Selection", exitscript=True)

fab_parts = []
has_invalid_elements = False
pinned_count = 0

# Filter selection to isolate unpinned MEP Fabrication Parts
for elem in selection:
    if not isinstance(elem, FabricationPart):
        has_invalid_elements = True
    elif elem.Pinned:
        # Pinned elements cannot be modified by the API and would fail silently
        pinned_count += 1
    else:
        fab_parts.append(elem)

# Nothing left to process: explain why in a single dialog and stop
if not fab_parts:
    if not pinned_count:
        msg = "No MEP Fabrication Parts in the selection. Nothing to rotate."
    elif not has_invalid_elements:
        msg = ("All {0} selected Fabrication Part(s) are pinned.\n"
               "Unpin them and run the command again.".format(pinned_count))
    else:
        msg = ("No valid MEP Fabrication Parts to rotate.\n"
               "{0} pinned Fabrication Part(s) and other non-Fabrication elements "
               "were skipped.".format(pinned_count))
    forms.alert(msg, title="Nothing to Rotate", warn_icon=True, exitscript=True)

# Valid parts remain: warn about what will be skipped, then proceed
warnings = []
if has_invalid_elements:
    warnings.append("Some selected elements are not MEP Fabrication Parts and will be skipped.")
if pinned_count:
    warnings.append(
        "{0} selected Fabrication Part(s) are pinned and will be skipped.\n"
        "Unpin them and run the command again.".format(pinned_count)
    )

if warnings:
    forms.alert("\n\n".join(warnings), title="Invalid Selection Warning", warn_icon=True)

# -90 degrees for clockwise rotation
angle = -math.radians(90)

count = 0
failed = 0

with revit.Transaction("Rotate Fabrication Parts 90° CW"):
    for elem in fab_parts:
        axis = None
        cm = elem.ConnectorManager

        origins = []
        if cm:
            for c in cm.Connectors:
                try:
                    origins.append(c.Origin)
                except Exception:
                    pass

        if len(origins) >= 2:
            p1 = origins[0]
            p2 = origins[1]

            # Normalize vector direction to force a consistent axis in global space
            vec = p2 - p1
            if (vec.X < -0.0001) or \
               (abs(vec.X) < 0.0001 and vec.Y < -0.0001) or \
               (abs(vec.X) < 0.0001 and abs(vec.Y) < 0.0001 and vec.Z < -0.0001):
                p1, p2 = p2, p1

            if p1.DistanceTo(p2) > 0.001:
                axis = Line.CreateBound(p1, p2)

        # Fallback to vertical axis if no connector axis is defined
        if not axis:
            loc = elem.Location
            if isinstance(loc, LocationPoint):
                pt = loc.Point
                pt2 = XYZ(pt.X, pt.Y, pt.Z + 1.0)
                axis = Line.CreateBound(pt, pt2)

        if axis:
            try:
                ElementTransformUtils.RotateElement(doc, elem.Id, axis, angle)
                count += 1
            except Exception:
                failed += 1
        else:
            failed += 1

# Report the result
if failed:
    forms.alert(
        "{0} element(s) successfully rotated!\n{1} element(s) could not be rotated.".format(count, failed),
        title="Rotate 90° CW",
        warn_icon=True
    )
else:
    forms.alert("{0} element(s) successfully rotated!".format(count), title="Success")

