# -*- coding: utf-8 -*-
__title__ = "Views By Scope Box"
__author__ = "Bruno Dias"
__doc__ = """How to use:

- Select from the list a View to be duplicated (Floor Plan).
- Choose the target Scope Boxes.
- Define a prefix for the sheets. The final name will be: [Prefix] + [Scope Box Name].
"""

__min_revit_ver__ = 2023
__max_revit_ver__ = 2026

from Autodesk.Revit.DB import *
from pyrevit import forms, revit

# Variáveis globais
doc = revit.doc
uidoc = revit.uidoc

###### Functions ########

def filter_floor_plans(view):
    """Filtra apenas plantas de piso que não são templates."""
    if view.IsTemplate:
        return False
    # ViewType.FloorPlan é estável em todas as versões
    if view.ViewType == ViewType.FloorPlan:
        return True
    return False

def get_all_scope_boxes():
    """Coleta todas as Scope Boxes do projeto (OST_VolumeOfInterest)."""
    return FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_VolumeOfInterest).ToElements()

###### Main Execution ########

base_view = forms.select_views(
    title='Select Base Floor Plan',
    multiple=False,
    filterfunc=filter_floor_plans
)

if base_view:
    scope_boxes = get_all_scope_boxes()
    
    if scope_boxes:
        selected_scopes = forms.SelectFromList.show(
            scope_boxes,
            name_attr='Name',
            title='Select Target Scope Boxes',
            multiselect=True
        )

        if selected_scopes:
            prefix = forms.ask_for_string(
                default='L1 - MECHANICAL PIPE LAYOUT - AREA ',
                title='View Name Prefix',
                prompt='Enter the prefix for the new views:'
            )

            if prefix:
                t = Transaction(doc, "ViewsByScopeBox")
                t.Start()

                try:
                    for sb in selected_scopes:
                        # Duplicação comum sem detalhes ou dependência
                        new_view_id = base_view.Duplicate(ViewDuplicateOption.Duplicate)
                        new_view = doc.GetElement(new_view_id)
                        
                        # Nomeando a vista
                        try:
                            new_view.Name = prefix + sb.Name
                        except:
                            new_view.Name = prefix + sb.Name + "_Copy"

                        param_scope = new_view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP)

                        if param_scope and not param_scope.IsReadOnly:
                            param_scope.Set(sb.Id)
                    
                    t.Commit()
                    forms.alert("Success! Views created.", title="Done")
                
                except Exception as e:
                    t.RollBack()
                    print("Erro na execução: {}".format(e))
#                     forms.alert("Error. Transaction rolled back.")
#             else:
#                 forms.alert("Prefix is required.")
#         else:
#             forms.alert("No Scope Boxes selected.")
#     else:
#         forms.alert("No Scope Boxes found.")
# else:
#     forms.alert("No base view selected.")