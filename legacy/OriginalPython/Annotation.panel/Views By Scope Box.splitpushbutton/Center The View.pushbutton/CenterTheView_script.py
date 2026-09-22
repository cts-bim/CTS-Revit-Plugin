# -*- coding: utf-8 -*-
__title__ = "Center the View"
__doc__ = """How to use:

- Open a Sheet View.
- Run this command.
- The script will center the View on the sheet and align the title below.
"""

__author__ = "Bruno Dias"     #Description of the button displayed in Revit UI
__min_revit_ver__= 2023
__max_revit_ver__ = 2026

import os
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

def get_view_viewport(view_sheet):

    if not isinstance(view_sheet, ViewSheet):
        #forms.alert("A vista ativa não é uma folha (ViewSheet).", title="Center View")
        return None

    viewports = FilteredElementCollector(doc,view_sheet.Id).OfCategory(BuiltInCategory.OST_Viewports).ToElements()
    lf = []

    for viewport in viewports:
        vp_ele = doc.GetElement(viewport.ViewId)
        viewport_type = vp_ele.ViewType
        if viewport_type != viewport_type.Legend:
            lf.append(viewport)

    if len(lf) != 1:
        #forms.alert("erro", title="Center View")
        return None

    return lf[0]


def get_view_title_block(view_sheet):

    if not isinstance(view_sheet, ViewSheet):
        #forms.alert("A vista ativa não é uma folha (ViewSheet).", title="Center View")
        return None

    placed_instances = FilteredElementCollector(doc,view_sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).ToElements()

    if len(placed_instances) != 1:
        #forms.alert("erro", title="Center View")
        return None
    
    return placed_instances[0]

def label_pos(viewport):
    """Centraliza a label da viewport horizontalmente e posiciona abaixo da vista."""
    
    # Bounding box da viewport
    vp_box = viewport.GetBoxOutline()
    vp_box_min = vp_box.MinimumPoint
    vp_box_max = vp_box.MaximumPoint

    # Bounding box da lable
    vp_lable = viewport.GetLabelOutline()
    vp_lable_min = vp_lable.MinimumPoint
    vp_lable_max = vp_lable.MaximumPoint

    # Largura da box viewport
    vp_box_width = vp_box_max.X - vp_box_min.X

    # Largura da lable viewport
    vp_lable_width = vp_lable_max.X - vp_lable_min.X


    # Deslocamento vertical (abaixo da viewport)
    offset_x = ((vp_box_width - vp_lable_width) / 2)  

    # Novo offset relativo ao centro da viewport
    new_offset = XYZ(offset_x, -0.05, 0)

    return new_offset
    

def check_viewport_title(viewport):
    # Confirma se a Viewport tem Title ou não
    vp_type = doc.GetElement(viewport.GetTypeId())
    title_param = vp_type.get_Parameter(BuiltInParameter.VIEWPORT_ATTR_LABEL_TAG).AsElementId()

    if title_param == ElementId.InvalidElementId:
        return False
    else:
        return True


###### Main Execution ########
import traceback
try:

# Info Viewport
    view_viewport = get_view_viewport(active_view)
    view_viewport_center = view_viewport.GetBoxCenter()

#info TitleBlock
    view_title_block = get_view_title_block(active_view)
    view_tb_point = view_title_block.Location.Point

    sheet_width = view_title_block.get_Parameter(BuiltInParameter.SHEET_WIDTH).AsDouble() - 0.44
    sheet_height = view_title_block.get_Parameter(BuiltInParameter.SHEET_HEIGHT).AsDouble() 

    #calculation
    tb_center = view_tb_point + XYZ(sheet_width / 2, sheet_height / 2, 0) # Location do TitleBlock é na base inferior esquerda dele. Por isso somo + metade da folha em X e Y

    offset = tb_center - view_viewport_center 

###### Transaction ########

    t = Transaction(doc, "Center the View")
    t.Start()
    # posiciona a view no meio da viewport
    view_viewport.Location.Move(offset)

    if check_viewport_title(view_viewport):
        # posiciona a lable embaixo da view
        new_label = label_pos(view_viewport)
        view_viewport.LabelOffset = new_label

    t.Commit()

except Exception as ex:
    forms.alert(
        "Execution error:\n\n{}".format(ex),
        title="Center the View"
    )



