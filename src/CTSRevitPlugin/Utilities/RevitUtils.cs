using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CTSRevitPlugin.Utilities
{
    public static class RevitUtils
    {
        public static void Alert(string message, string title = "CTS Tools")
        {
            CtsMessageWindow.Show(title, message);
        }

        public static List<Element> SelectedElements(UIDocument uidoc)
        {
            return uidoc.Selection.GetElementIds()
                .Select(id => uidoc.Document.GetElement(id))
                .Where(e => e != null)
                .ToList();
        }

        public static Element PickElement(UIDocument uidoc, string prompt)
        {
            try
            {
                Reference r = uidoc.Selection.PickObject(ObjectType.Element, prompt);
                return uidoc.Document.GetElement(r);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }

        public static XYZ RepresentativePoint(Element e)
        {
            var fi = e as FamilyInstance;
            if (fi != null && fi.Location is LocationPoint lp) return lp.Point;

            var locCurve = e.Location as LocationCurve;
            if (locCurve != null) return (locCurve.Curve.GetEndPoint(0) + locCurve.Curve.GetEndPoint(1)) / 2.0;

            var box = e.get_BoundingBox(null);
            if (box != null) return (box.Min + box.Max) / 2.0;

            return null;
        }

        public static ConnectorSet GetConnectors(Element e)
        {
            try
            {
                var cm = e.GetType().GetProperty("ConnectorManager", BindingFlags.Public | BindingFlags.Instance)?.GetValue(e, null);
                if (cm != null)
                {
                    var p = cm.GetType().GetProperty("Connectors");
                    return p?.GetValue(cm, null) as ConnectorSet;
                }

                var getConnectorManager = e.GetType().GetMethod("GetConnectorManager");
                if (getConnectorManager != null)
                {
                    var obj = getConnectorManager.Invoke(e, null);
                    return obj?.GetType().GetProperty("Connectors")?.GetValue(obj, null) as ConnectorSet;
                }
            }
            catch { }
            return null;
        }

        public static List<Connector> PhysicalConnectors(Element e)
        {
            var set = GetConnectors(e);
            var list = new List<Connector>();
            if (set == null) return list;

            foreach (Connector c in set)
            {
                try
                {
                    if (c.ConnectorType == ConnectorType.End || c.ConnectorType == ConnectorType.Curve)
                        list.Add(c);
                }
                catch { }
            }
            return list;
        }

        public static Connector NearestConnector(Element a, Element b, out double distance)
        {
            distance = double.MaxValue;
            Connector best = null;
            var pa = PhysicalConnectors(a);
            var pb = PhysicalConnectors(b);
            foreach (var ca in pa)
                foreach (var cb in pb)
                {
                    double d = ca.Origin.DistanceTo(cb.Origin);
                    if (d < distance)
                    {
                        distance = d;
                        best = ca;
                    }
                }
            return best;
        }

        public static ConnectorPair NearestConnectorPair(Element a, Element b)
        {
            double best = double.MaxValue;
            Connector caBest = null, cbBest = null;
            foreach (var ca in PhysicalConnectors(a))
                foreach (var cb in PhysicalConnectors(b))
                {
                    double d = ca.Origin.DistanceTo(cb.Origin);
                    if (d < best)
                    {
                        best = d; caBest = ca; cbBest = cb;
                    }
                }
            return caBest == null ? null : new ConnectorPair(caBest, cbBest, best);
        }

        public sealed class ConnectorPair
        {
            public Connector A { get; }
            public Connector B { get; }
            public double Distance { get; }
            public ConnectorPair(Connector a, Connector b, double distance)
            {
                A = a; B = b; Distance = distance;
            }
        }

        public static bool InvokeBool(object target, string methodName, params object[] args)
        {
            try
            {
                var m = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                if (m == null) return false;
                var result = m.Invoke(target, args);
                return result is bool b ? b : true;
            }
            catch { return false; }
        }

        public static object Invoke(object target, string methodName, params object[] args)
        {
            try
            {
                var m = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                return m?.Invoke(target, args);
            }
            catch { return null; }
        }
    }
}
