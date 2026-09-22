# -*- coding: utf-8 -*-
__title__ = "Rod Length\nAdjuster"
__author__ = "Pedro Oliveira"
__doc__ = """How to use:

- Select the MEP Fabrication Hangers you want to adjust.
- Run the command.
- Enter a value to lengthen (+) or shorten (-) the rod length (e.g., 1' 6" or -1' 10 5/8").
"""

__min_revit_ver__ = 2023
__max_revit_ver__ = 2026

import clr
from pyrevit import revit, DB, forms
from Autodesk.Revit.DB import UnitFormatUtils, SpecTypeId, Transaction, BuiltInCategory

doc = revit.doc
raw_selection = list(revit.get_selection())

if not raw_selection:
    forms.alert("Please select at least one element in the model.", exitscript=True)

# 1. Separate MEP Fabrication Hangers from non-hanger elements
hangers = []
skipped_count = 0

for elem in raw_selection:
    if elem.Category and elem.Category.Id.IntegerValue == int(BuiltInCategory.OST_FabricationHangers):
        hangers.append(elem)
    else:
        skipped_count += 1

if not hangers:
    forms.alert("None of the selected elements belong to the MEP Fabrication Hangers category.", exitscript=True)

# 2. Prompt user input via pyRevit dialog
user_input = forms.ask_for_string(
    prompt="Enter value to adjust (+ to increase, - to decrease, e.g., 1' 6\" or -1' 10 5/8\"): ",
    title="Adjust Rod Length"
)

def parse_imperial_to_feet(input_str):
    """Converts an imperial text string (positive or negative) into decimal feet."""
    if not input_str:
        return None

    s = input_str.strip()
    is_negative = False

    # Handle explicit plus or minus signs
    if s.startswith("-"):
        is_negative = True
        s = s[1:].strip()
    elif s.startswith("+"):
        s = s[1:].strip()

    # Attempt parsing via native Revit API (UnitFormatUtils)
    try:
        units = doc.GetUnits()
        parsed, value = UnitFormatUtils.TryParse(units, SpecTypeId.Length, s)
        if parsed:
            return -value if is_negative else value
    except Exception:
        pass

    # Fallback manual parsing in Python
    try:
        clean_s = s.replace('"', '')
        feet = 0.0
        inches = 0.0
        
        if "'" in clean_s:
            parts = clean_s.split("'")
            feet = float(parts[0].strip()) if parts[0].strip() else 0.0
            inch_part = parts[1].strip()
        else:
            inch_part = clean_s
            
        if inch_part:
            if " " in inch_part:
                whole, frac = inch_part.split()
                num, den = frac.split('/')
                inches = float(whole) + (float(num) / float(den))
            elif "/" in inch_part:
                num, den = inch_part.split('/')
                inches = float(num) / float(den)
            else:
                inches = float(inch_part)
                
        total_feet = feet + (inches / 12.0)
        return -total_feet if is_negative else total_feet
    except Exception:
        return None

if user_input:
    delta_length = parse_imperial_to_feet(user_input)
    
    if delta_length is None or delta_length == 0:
        forms.alert("Invalid input value. Accepted examples: 1' 6\", -1' 10 5/8\", -6\"", exitscript=True)

    # 3. Start Revit transaction to modify rod length
    with Transaction(doc, "Adjust Rod Length") as t:
        t.Start()
        modified_count = 0
        for hanger in hangers:
            rod_info = hanger.GetRodInfo()
            if rod_info and rod_info.RodCount > 0:
                # Disable auto-hosting to enable manual rod length editing
                if rod_info.CanRodsBeHosted:
                    rod_info.CanRodsBeHosted = False

                for i in range(rod_info.RodCount):
                    current_len = rod_info.GetRodLength(i)
                    new_len = current_len + delta_length
                    
                    if new_len > 0:
                        rod_info.SetRodLength(i, new_len)
                        modified_count += 1
                    else:
                        forms.alert("The specified value results in a zero or negative rod length on one of the hangers.")
        t.Commit()
        
    # 4. Summary feedback report
    if modified_count > 0:
        msg = "Successfully adjusted {} MEP Fabrication Hanger(s).".format(len(hangers))
        if skipped_count > 0:
            msg += "\n\nSkipped {} non-hanger element(s).".format(skipped_count)
            
        forms.alert(msg, warn_icon=False)