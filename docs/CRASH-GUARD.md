# Crash guard e regras de UI

Este documento existe porque o plugin matava o Revit e o journal não dizia porquê.
Leia-se antes de mexer em qualquer dockable pane.

## O problema de fundo

O Revit corre a UI dos add-ins no seu próprio dispatcher WPF. Uma exceção que
escape de um handler WPF aí **não é mostrada a ninguém**: o CLR derruba o processo
e o journal do Revit regista apenas

```
ExceptionCode=0xe0434352
```

que é o código genérico de exceção gerida — sem tipo, sem mensagem, sem stack.
Sem mais nada, é um beco sem saída para depurar.

## `Utilities/CtsCrashGuard.cs`

Instalado como primeira instrução do `App.OnStartup`, antes de existir qualquer
pane. Engancha três coisas:

| Hook | Para quê |
|---|---|
| `Dispatcher.CurrentDispatcher.UnhandledException` | exceções que chegam ao pump WPF do Revit |
| `AppDomain.CurrentDomain.UnhandledException` | as que escapam a tudo |
| `AppDomain.CurrentDomain.FirstChanceException` | o local exato do throw, filtrado às nossas frames |

Escreve tipo, mensagem, `TargetSite`, stack e inner exceptions em:

```
%LOCALAPPDATA%\CTSRevitPlugin\crash.log
```

### Política

- **Exceção do AvalonDock** → tratada (`e.Handled = true`). Ver a secção seguinte.
- **Exceção com frames `CTSRevitPlugin`** → tratada e registada. Um bug nosso vira
  uma linha de log em vez de levar a sessão do utilizador.
- **Qualquer outra** → registada e deixada passar. Engolir a exceção do Revit ou de
  outro add-in esconderia problemas reais.

O log tem limite de 2 MB e é reciclado. O logger nunca lança: um logger que falha
durante um crash é pior do que nenhum.

## O defeito do AvalonDock

O AvalonDock é a biblioteca de docking que o Revit usa para os dockable panes.
Reordena separadores à medida que o rato passa por cima deles. Quando um painel é
flutuado, fechado ou re-dockado, a coleção de separadores muda por baixo desse
handler, e o mouse-enter seguinte chama `MoveChild` com um índice que já não existe:

```
System.ArgumentOutOfRangeException: Index was out of range.
   at System.Collections.ObjectModel.ObservableCollection`1.MoveItem(Int32, Int32)
   at Xceed.Wpf.AvalonDock.Layout.LayoutGroup`1.MoveChild(Int32 oldIndex, Int32 newIndex)
   at Xceed.Wpf.AvalonDock.Controls.LayoutAnchorableTabItem.OnMouseEnter(MouseEventArgs e)
   ... MouseDevice.ChangeMouseOver ... HwndSource.InputFilterMessage ...
```

Não há uma única frame `CTSRevitPlugin` nesse stack. O defeito é do framework de UI
do Revit e reproduz-se com dois ou mais panes no mesmo grupo de separadores: basta
o double-click para flutuar um deles. Um add-in não tem como impedir que seja
lançada.

O único trabalho que a chamada falhada faria era reordenar um separador debaixo do
cursor, por isso `IsDockingLayoutBug` reconhece-a e o guard marca-a como tratada.
Custa um movimento cosmético de tab e salva a sessão. O match é estreito de
propósito — só `ArgumentOutOfRangeException` / `IndexOutOfRangeException` /
`InvalidOperationException` **e** com uma frame `AvalonDock` no stack.

Reportável à Autodesk (Revit 2025.4).

## Regras ao escrever panes

**1. Nunca chamar a API do Revit a partir de um handler WPF.**
`IsVisibleChanged`, `Click`, `DispatcherTimer.Tick`, callbacks de diálogos — nada
disso é contexto de API válido. A única entrada segura é `ExternalEvent.Raise()`;
o trabalho faz-se no `Execute()`. Ver `RequestRefreshInApiContext()` no
`ParametersPane` e no `MechanicalPropertiesPane`.

**2. Nunca guardar um `Document` num campo.**
Um documento fechado continua a parecer um objeto vivo em C#, e o primeiro acesso
depois disso é um access violation — que nenhum `catch` apanha. Resolver sempre do
`ActiveUIDocument` na altura de usar, ou validar com `IsValidObject`. O
`ControlledApplication.DocumentClosing` em `App.cs` limpa as caches dos panes.

**3. Nunca usar `FindResource`.**
Lança quando a chave não existe, e isso acontece: o tema é trocado em runtime e há
um instante em que o dicionário está a ser substituído. Usar
`RevitThemeService.ThemeBrush(this, key)` e `RevitThemeService.StyleOrNull(this, key)`,
que assentam em `TryFindResource` e têm fallbacks literais.

**4. Todo o método chamado de um handler WPF ou de um evento do Revit precisa de
try/catch.**
Padrão usado: o método público faz try/catch e delega em `XCore()` com o corpo real
— ver `RebuildTiles` / `RebuildTilesCore` no `AssembliesPane`.

## Quando algo correr mal

1. Ler `%LOCALAPPDATA%\CTSRevitPlugin\crash.log`.
2. Se houver entrada `DISPATCHER (handled - CTS frames present)`, o stack aponta
   para o nosso bug e o Revit sobreviveu.
3. Se houver `DISPATCHER (not ours - left alone)`, ler o stack: é de outro
   componente, e o Revit provavelmente morreu.
4. O journal do Revit (`%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit <ver>\Journals\`)
   dá o contexto — que ação do utilizador precedeu a falha.
