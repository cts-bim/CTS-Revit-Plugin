# -*- coding: utf-8 -*-
__title__ = "Flip Elements"
__doc__ = """How to use:

- Select all desired MEP Fabrication Parts.
- Run the command.
- All selected Fabrication Parts will be flipped.
"""

__author__ = "Pedro Oliveira"
__min_revit_ver__ = 2023
__max_revit_ver__ = 2026

from pyrevit import revit, forms
from Autodesk.Revit.DB import FabricationPart

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
        msg = "No MEP Fabrication Parts in the selection. Nothing to flip."
    elif not has_invalid_elements:
        msg = ("All {0} selected Fabrication Part(s) are pinned.\n"
               "Unpin them and run the command again.".format(pinned_count))
    else:
        msg = ("No valid MEP Fabrication Parts to flip.\n"
               "{0} pinned Fabrication Part(s) and other non-Fabrication elements "
               "were skipped.".format(pinned_count))
    forms.alert(msg, title="Nothing to Flip", warn_icon=True, exitscript=True)

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

count = 0
failed = 0

# Process flipping action on valid Fabrication Parts
with revit.Transaction("Flip Fabrication Parts in Batch"):
    for elem in fab_parts:
        try:
            elem.Flip()
            count += 1
        except Exception:
            failed += 1

# Report the result
if failed:
    forms.alert(
        "{0} element(s) successfully flipped!\n{1} element(s) could not be flipped.".format(count, failed),
        title="Flip Elements",
        warn_icon=True
    )
else:
    forms.alert("{0} element(s) successfully flipped!".format(count), title="Success")