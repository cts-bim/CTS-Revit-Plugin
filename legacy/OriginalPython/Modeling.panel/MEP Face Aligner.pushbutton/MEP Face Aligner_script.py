# -*- coding: utf-8 -*-
__title__ = "MEP Face Aligner"
__doc__ = """How to use:

- Select the elements you want to MOVE (Pipes or Fabrication Pipes).
- Run this command.
- Choose the alignment side (TOP, BOTTOM, LEFT, or RIGHT) relative to your screen.
- Pick the REFERENCE element.
"""

__author__ = "Bruno Dias"
__min_revit_ver__ = 2023
__max_revit_ver__ = 2026

from Autodesk.Revit.DB import *
from Autodesk.Revit.DB.Plumbing import Pipe, PipeInsulation
from pyrevit import revit, forms, script

doc = __revit__.ActiveUIDocument.Document
uidoc = __revit__.ActiveUIDocument
active_view = doc.ActiveView


def get_valid_selection():
    """Filtra a seleção para pegar apenas tubos (Nativos/Fabrication) ou seus isolamentos."""
    selection_ids = uidoc.Selection.GetElementIds()
    valid_elements = {} # Usamos um dicionário para evitar duplicatas (mesmo ID)

    for eid in selection_ids:
        el = doc.GetElement(eid)
        
        # 1. Se for Pipe nativo
        if isinstance(el, Pipe):
            valid_elements[el.Id] = el
            
        # 2. Se for Fabrication Part (Filtra apenas tubos)
        elif isinstance(el, FabricationPart):
            if "Pipe" in el.ProductName:
                valid_elements[el.Id] = el
                
        # 3. Se for Isolamento, pega o Tubo Hospedeiro
        elif isinstance(el, PipeInsulation):
            host_id = el.HostElementId
            host_el = doc.GetElement(host_id)
            if host_el:
                valid_elements[host_el.Id] = host_el
                
    return list(valid_elements.values())

def get_total_radius(el):
    """Calcula o raio + isolamento buscando o elemento dependente (PipeInsulation)."""
    radius = 0
    insulation = 0
    doc = el.Document

    if isinstance(el, Pipe):
        # 1. Diâmetro Externo do Tubo (Sempre BuiltIn)
        p_od = el.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER)
        if p_od:
            radius = p_od.AsDouble() / 2.0
            
        # 2. Buscar Isolamento via Dependent Elements
        dependent_ids = el.GetDependentElements(ElementClassFilter(PipeInsulation))
        if dependent_ids:
            # Pegamos o primeiro elemento de isolamento encontrado
            ins_el = doc.GetElement(dependent_ids[0])
            # Usamos o parâmetro correto do elemento de isolamento (não do pipe)
            p_thick = ins_el.get_Parameter(BuiltInParameter.RBS_INSULATION_THICKNESS_FOR_PIPE)
            if p_thick:
                insulation = p_thick.AsDouble()

    elif isinstance(el, FabricationPart):
        p_od = el.get_Parameter(BuiltInParameter.FABRICATION_PART_DIAMETER_OUT)
        if p_od:
            radius = p_od.AsDouble() / 2.0
        # Fabrication Parts guardam a espessura diretamente na propriedade
        try:
            insulation = el.InsulationThickness
        except:
            insulation = 0
            
    return radius + insulation

def get_connectors(el):
    """
    Acessa o ConnectorManager e retorna apenas conectores físicos (End ou Curve).
    Funciona para Pipes, Fittings e Fabrication Parts.
    """
    connectors_list = []
    
    # 1. Tenta acessar o ConnectorManager (padrão para Pipes e Fabrication)
    cm = None
    try:
        if hasattr(el, "ConnectorManager"):
            cm = el.ConnectorManager
        elif hasattr(el, "MEPModel") and el.MEPModel:
            cm = el.MEPModel.ConnectorManager
            
        if cm:
            for conn in cm.Connectors:
                # Filtra apenas conectores físicos (ignora lógicos/analíticos)
                if conn.ConnectorType in [ConnectorType.End, ConnectorType.Curve]:
                    connectors_list.append(conn)
    except Exception:
        pass
        
    return connectors_list


def get_extreme_connector(el, view_direction, find_max=True):
    """
    Encontra o ponto do conector mais extremo em uma dada direção.
    find_max=True busca o 'Top', find_max=False busca o 'Bottom'.
    """
    connectors = get_connectors(el)
    
    if not connectors:
        return None

    # O segredo: Projetamos a posição do conector no vetor da vista
    # Isso transforma a posição XYZ em um número escalar para comparação
    ponto_vencedor = None
    valor_extremo = float('-inf') if find_max else float('inf')

    for conn in connectors:
        ponto = conn.Origin
        # Produto escalar (DotProduct) nos diz a 'altura' do ponto na direção da vista
        projecao = ponto.DotProduct(view_direction)

        if find_max:
            if projecao > valor_extremo:
                valor_extremo = projecao
                ponto_vencedor = ponto
        else:
            if projecao < valor_extremo:
                valor_extremo = projecao
                ponto_vencedor = ponto

    return ponto_vencedor

def get_shell_elevation(el, direction_vector, is_max=True):
    """
    Retorna a coordenada (em Decimal Feet) da borda externa do isolamento.
    - direction_vector: Pode ser o UpDirection (Top/Bottom) ou RightDirection (Left/Right).
    - is_max: True para a borda 'positiva' (Top/Right), False para a 'negativa' (Bottom/Left).
    """
    # 1. Acha o conector que está mais "à frente" ou "atrás" no vetor
    ponto_eixo = get_extreme_connector(el, direction_vector, find_max=is_max)
    
    if not ponto_eixo:
        return None
        
    # 2. Pega o raio + isolamento
    total_r = get_total_radius(el)
    
    # 3. Calcula a projeção do eixo
    projecao_eixo = ponto_eixo.DotProduct(direction_vector)
    
    # 4. Soma ou subtrai o raio na direção escolhida
    if is_max:
        return projecao_eixo + total_r
    else:
        return projecao_eixo - total_r
    

# ------------------------------------------------------------------
# MAIN EXECUTION
# ------------------------------------------------------------------

# 1. Selection Validation
selected_elements = get_valid_selection()
if not selected_elements:
    forms.alert("No valid Pipes or Fabrication Pipes selected.", title="Selection Error")
    script.exit()


# 2. Bloqueio de 3D Rotacionado (Oblíquo)
if isinstance(active_view, View3D):
    view_dir = active_view.ViewDirection
    # Verificamos se a câmera está perfeitamente alinhada a um eixo X, Y ou Z
    # Em uma vista reta, dois dos componentes (x, y, z) do vetor devem ser zero (ou quase zero)
    is_straight = any(abs(getattr(view_dir, axis)) > 0.999 for axis in ['X', 'Y', 'Z'])
    
    if not is_straight:
        forms.alert("3D View must be oriented to a face of the ViewCube (Top, Front, Left, etc.).", 
                    title="Orientation Error")
        script.exit()


# 3. UI - Choose Direction
options = ["TOP", "BOTTOM", "LEFT", "RIGHT"]
choice = forms.ask_for_one_item(options, default="BOTTOM", title="Select alignment direction:")
    

# 4. Reference Selection
with forms.WarningBar(title="Pick the reference element (Pipe or Insulation)"):
    try:
        picked_ref = revit.pick_element()
    except:
        picked_ref = None

if not picked_ref:
    forms.alert("Reference element not selected.", title="Cancelled")
    script.exit()
ref_el = None

# Se o usuário clicar direto no Pipe ou Fabrication
if isinstance(picked_ref, (Pipe, FabricationPart)):
    ref_el = picked_ref

# Se o usuário clicar no Isolamento, nós "traduzimos" para o Pipe dono dele
elif isinstance(picked_ref, PipeInsulation):
    host_id = picked_ref.HostElementId
    ref_el = doc.GetElement(host_id)


# 5. Logical Protection for Reference (Agora validando o ref_el traduzido)
if not ref_el:
    forms.alert("The reference must be a valid Pipe, Fabrication element, or Pipe Insulation.", title="Reference Error")
    script.exit()


# 6. Setup Vectors and Logic
up = active_view.UpDirection
right = active_view.RightDirection

if choice in ["TOP", "BOTTOM"]:
    v_dir = up
    is_max = (choice == "TOP")
else:
    v_dir = right
    is_max = (choice == "RIGHT")


# 7. Compute Reference Elevation (Shell)
ref_shell = get_shell_elevation(ref_el, v_dir, is_max)

if ref_shell is None:
    forms.alert("Could not calculate the reference shell elevation.", title="Calculation Error")
    script.exit()


# ------------------------------------------------------------------
# TRANSACTION
# ------------------------------------------------------------------
t = Transaction(doc, "Align by " + choice)
t.Start()

try:
    for el in selected_elements:
        # Pula se for o próprio elemento de referência
        if el.Id == ref_el.Id: 
            continue
        
        # Calcula a cota da "casca" do elemento atual
        current_shell = get_shell_elevation(el, v_dir, is_max)
        
        if current_shell is not None:
            # Distância entre a casca atual e a casca de referência
            delta = ref_shell - current_shell
            
            # Cria o vetor de translação
            move_vec = v_dir.Multiply(delta)
            
            if not move_vec.IsZeroLength():
                try:
                    ElementTransformUtils.MoveElement(doc, el.Id, move_vec)
                except Exception as ex:
                    # Reporta erro de conexão ou restrição para elementos individuais
                    print("Could not move element {}: {}".format(el.Id, ex))
    
    t.Commit()
    
except Exception as global_ex:
    t.RollBack()
    forms.alert("A critical error occurred: {}".format(global_ex), title="Transaction Failed")
