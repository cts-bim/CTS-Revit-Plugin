# -*- coding: utf-8 -*-
__title__ = "Views To Sheet"
__doc__ = """How to use:

- Run this tool. 
- Select the views you want to place.
- Enter the Sheet Number prefix (e.g., ARQ).
- Select the Title Block to use.
"""

__author__ = "Bruno Dias" 
__min_revit_ver__= 2023
__max_revit_ver__ = 2026

import re
from Autodesk.Revit.DB import *
from Autodesk.Revit.UI import *
from pyrevit import forms, revit, script

import clr
clr.AddReference('System')
from System.Collections.Generic import List

###### Variables ########

doc = __revit__.ActiveUIDocument.Document
uidoc = __revit__.ActiveUIDocument
app = __revit__.Application
active_view = doc.ActiveView

###### Functions ########

def title_block_middle(tb):
    # Defining center point of TitleBlock
    if isinstance(tb, FamilyInstance):
        tb_point = tb.Location.Point
        tb_width = tb.get_Parameter(BuiltInParameter.SHEET_WIDTH).AsDouble() - 0.44
        tb_height = tb.get_Parameter(BuiltInParameter.SHEET_HEIGHT).AsDouble() 
        tb_center = tb_point + XYZ(tb_width / 2, tb_height / 2, 0) 
        return tb_center
    else:
        return XYZ(0,0,0)

def normalize_scope_box(name):
    # Standardize Scope Box names: uppercase and drop a leading "AREA"
    # "Area A" / "area a" / "AreaB" / "Area-C" / "D"  ->  "A" / "B" / "C" / "D"
    if not name:
        return name

    clean = name.strip().upper()

    # Remove the leading "AREA" plus any separator that follows it
    stripped = re.sub(r'^AREA[\s_\-\.]*', '', clean)

    # If nothing is left (Scope Box literally named "Area"), keep the original name
    if not stripped:
        return clean

    # Collapse redundant inner whitespace
    return re.sub(r'\s+', ' ', stripped)

def sheetnumber(view, existing_list, prefix_user):
    # Pattern: PREFIX + SCOPEBOX NAME
    try:
        # Get Scope Box Element
        sb_id = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP).AsElementId()

        if sb_id != ElementId.InvalidElementId:
            view_scope_box = normalize_scope_box(doc.GetElement(sb_id).Name)
        else:
            view_scope_box = "General"

        # Combine Prefix + Scope Box
        sheet_num_base = "{}{}".format(prefix_user, view_scope_box)

    except Exception as e:
        # Fallback in case of error
        print("Error generating number: {}".format(e)) 
        sheet_num_base = prefix_user + "-ERR"

    # Avoid duplicate Sheet Numbers
    final_sheet_number = sheet_num_base
    count = 1

    while final_sheet_number in existing_list:
        final_sheet_number = "{}_{}".format(sheet_num_base, count)
        count += 1

    existing_list.append(final_sheet_number)
    return final_sheet_number

###### Main Execution ########

views = forms.select_views(title='Select Views (Unplaced Plans and Sections)', 
                           filterfunc=lambda v: (v.ViewType in [ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.Section]
                           and not v.IsTemplate 
                           and (v.get_Parameter(BuiltInParameter.VIEWER_SHEET_NUMBER) 
                           and v.get_Parameter(BuiltInParameter.VIEWER_SHEET_NUMBER).AsString() == "---")))

if views:
    # Ask user for prefix in English
    prefix_input = forms.ask_for_string(
        default="M-L2-SD-", 
        prompt="Enter the Sheet Number prefix:", 
        title="Sheet Number Prefix"
    )
    
    if prefix_input:
        titleblock = forms.select_titleblocks()
        if titleblock:

            get_all_sheets = FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Sheets).WhereElementIsNotElementType().ToElements()
            existing_sheets = [sheet.SheetNumber for sheet in get_all_sheets]

            # Viewport.Name has a known IronPython quirk (raises AttributeError), so
            # the type name must be read through the Element.Name property descriptor
            def get_type_name(element_type):
                return Element.Name.__get__(element_type)

            # Cache for the "CTS" viewport type id, resolved once from the first
            # viewport we create (via GetValidTypes, the reliable way to list the
            # types actually assignable to a Viewport)
            cts_viewport_type_id = None
            cts_viewport_search_done = False

            ##### Transaction ########
            t = Transaction(doc, "Create Sheets from Views")
            t.Start()

            for view in views:
                view_name = view.Name

                # Create sheet and set identity
                new_sheet = ViewSheet.Create(doc, titleblock)
                new_sheet.Name = view_name

                # Apply the auto-generated Sheet Number
                new_sheet.SheetNumber = sheetnumber(view, existing_sheets, prefix_input)

                # Get Title Block instance on the new sheet to find its center
                tb_instance = FilteredElementCollector(doc, new_sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).FirstElement()

                if tb_instance:
                    tb_center = title_block_middle(tb_instance)
                    new_viewport = Viewport.Create(doc, new_sheet.Id, view.Id, tb_center)

                    if not cts_viewport_search_done:
                        for type_id in new_viewport.GetValidTypes():
                            candidate_name = get_type_name(doc.GetElement(type_id))
                            if "CTS" in candidate_name.upper():
                                cts_viewport_type_id = type_id
                                break
                        cts_viewport_search_done = True

                    if cts_viewport_type_id:
                        new_viewport.ChangeTypeId(cts_viewport_type_id)

            t.Commit()
            # forms.alert("Sheets created successfully!", title="Success")
        else:
            forms.alert("User must select a Title Block.", title="Warning")
    else:
        forms.alert("Operation cancelled: No prefix provided.", title="Cancelled")