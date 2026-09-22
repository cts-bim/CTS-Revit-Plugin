using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CTSRevitPlugin.UI.ElementFilter
{
    /// <summary>
    /// Turns a FilterDefinition into a list of matching elements.
    ///
    /// Everything is evaluated in memory rather than through Revit's own
    /// ParameterFilterElement, because the conditions here mix parameter kinds
    /// freely (a string Contains next to a numeric &gt;=) and may target parameters
    /// that only some of the collected elements carry.
    /// </summary>
    public static class ElementFilterEngine
    {
        public sealed class MatchResult
        {
            public List<ElementId> Matches { get; set; } = new List<ElementId>();
            public int Examined { get; set; }
        }

        /// <summary>
        /// Runs the filter. <paramref name="promptValues"/> supplies the values for
        /// conditions whose InputType is Prompt, keyed by parameter name.
        /// </summary>
        public static MatchResult Run(
            Document doc,
            View view,
            FilterDefinition filter,
            IDictionary<string, string> promptValues)
        {
            MatchResult result = new MatchResult();
            if (doc == null || filter == null) return result;

            List<FilterCondition> conditions = (filter.Conditions ?? new List<FilterCondition>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.ParameterName))
                .ToList();

            if (conditions.Count == 0) return result;

            FilteredElementCollector collector =
                filter.Scope == FilterScope.ActiveView && view != null
                    ? new FilteredElementCollector(doc, view.Id)
                    : new FilteredElementCollector(doc);

            collector = collector.WhereElementIsNotElementType();

            // Narrowing by category up front is far cheaper than reading parameters
            // off every element in the model.
            HashSet<long> categoryIds = new HashSet<long>(filter.CategoryIds ?? new List<long>());

            foreach (Element element in collector)
            {
                if (element == null) continue;

                try
                {
                    if (categoryIds.Count > 0)
                    {
                        if (element.Category == null) continue;
                        if (!categoryIds.Contains(element.Category.Id.IdValue())) continue;
                    }

                    result.Examined++;

                    if (Matches(element, filter, conditions, promptValues))
                        result.Matches.Add(element.Id);
                }
                catch
                {
                    // Reading Category or a parameter throws on a few element kinds.
                    // One bad element must not abort a whole-model run.
                }
            }

            return result;
        }

        private static bool Matches(
            Element element,
            FilterDefinition filter,
            List<FilterCondition> conditions,
            IDictionary<string, string> promptValues)
        {
            bool all = filter.Match == RuleMatch.All;

            foreach (FilterCondition condition in conditions)
            {
                bool hit = Evaluate(element, condition, promptValues);

                if (all && !hit) return false;
                if (!all && hit) return true;
            }

            return all;
        }

        private static bool Evaluate(
            Element element,
            FilterCondition condition,
            IDictionary<string, string> promptValues)
        {
            string actual = ReadParameter(element, condition.ParameterName);

            if (condition.Operator == RuleOperator.IsEmpty)
                return string.IsNullOrWhiteSpace(actual);

            if (condition.Operator == RuleOperator.IsNotEmpty)
                return !string.IsNullOrWhiteSpace(actual);

            // A missing parameter never matches a value comparison.
            if (actual == null) return false;

            string expected = condition.Value ?? "";

            if (condition.InputType == RuleInputType.Prompt && promptValues != null)
            {
                string supplied;
                if (promptValues.TryGetValue(condition.ParameterName ?? "", out supplied))
                    expected = supplied ?? "";
            }

            switch (condition.Operator)
            {
                case RuleOperator.Equals:
                    return string.Equals(actual.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

                case RuleOperator.NotEquals:
                    return !string.Equals(actual.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

                case RuleOperator.Contains:
                    return actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;

                case RuleOperator.NotContains:
                    return actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0;

                case RuleOperator.BeginsWith:
                    return actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase);

                case RuleOperator.EndsWith:
                    return actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase);

                case RuleOperator.Greater:
                case RuleOperator.GreaterOrEqual:
                case RuleOperator.Less:
                case RuleOperator.LessOrEqual:
                    return CompareNumbers(actual, expected, condition.Operator);

                default:
                    return false;
            }
        }

        private static bool CompareNumbers(string actual, string expected, RuleOperator op)
        {
            double left, right;
            if (!TryParseNumber(actual, out left)) return false;
            if (!TryParseNumber(expected, out right)) return false;

            switch (op)
            {
                case RuleOperator.Greater: return left > right;
                case RuleOperator.GreaterOrEqual: return left >= right;
                case RuleOperator.Less: return left < right;
                case RuleOperator.LessOrEqual: return left <= right;
                default: return false;
            }
        }

        /// <summary>
        /// Pulls the leading number out of a value string, so "3/4\"" or "12.5 mm"
        /// still compare numerically instead of silently failing.
        /// </summary>
        private static bool TryParseNumber(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            if (double.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                return true;

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            foreach (char c in text.Trim())
            {
                if (char.IsDigit(c) || c == '.' || c == '-' && builder.Length == 0) builder.Append(c);
                else if (builder.Length > 0) break;
            }

            return double.TryParse(
                builder.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Instance parameter first, then the type parameter of the same name, which
        /// is what people expect when filtering on things like a type mark.
        /// Returns null when neither exists.
        /// </summary>
        public static string ReadParameter(Element element, string parameterName)
        {
            if (element == null || string.IsNullOrWhiteSpace(parameterName)) return null;

            try
            {
                Parameter p = element.LookupParameter(parameterName);

                if (p == null)
                {
                    Element type = element.Document.GetElement(element.GetTypeId());
                    if (type != null) p = type.LookupParameter(parameterName);
                }

                if (p == null) return null;

                return ParameterText(p, element.Document);
            }
            catch
            {
                return null;
            }
        }

        private static string ParameterText(Parameter p, Document doc)
        {
            try
            {
                string value = p.AsValueString();
                if (!string.IsNullOrWhiteSpace(value)) return value;

                if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                if (p.StorageType == StorageType.Integer)
                    return p.AsInteger().ToString(CultureInfo.InvariantCulture);
                if (p.StorageType == StorageType.Double)
                    return p.AsDouble().ToString("0.######", CultureInfo.InvariantCulture);

                if (p.StorageType == StorageType.ElementId)
                {
                    ElementId id = p.AsElementId();
                    if (id == null || id == ElementId.InvalidElementId) return "";
                    Element referenced = doc.GetElement(id);
                    return referenced == null
                        ? id.IdValue().ToString(CultureInfo.InvariantCulture)
                        : referenced.Name;
                }
            }
            catch { }

            return "";
        }

        /// <summary>
        /// Parameter names available on the elements of the chosen categories, for the
        /// designer's dropdown. Sampling is capped because the list only needs to be
        /// representative, not exhaustive.
        /// </summary>
        public static List<string> CollectParameterNames(
            Document doc,
            View view,
            IList<long> categoryIds,
            int sampleLimit = 400)
        {
            List<string> names = new List<string>();
            if (doc == null) return names;

            try
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<long> wanted = new HashSet<long>(categoryIds ?? new List<long>());

                FilteredElementCollector collector = view != null
                    ? new FilteredElementCollector(doc, view.Id)
                    : new FilteredElementCollector(doc);

                int sampled = 0;

                foreach (Element element in collector.WhereElementIsNotElementType())
                {
                    if (sampled >= sampleLimit) break;
                    if (element == null || element.Category == null) continue;
                    if (wanted.Count > 0 && !wanted.Contains(element.Category.Id.IdValue())) continue;

                    sampled++;

                    try
                    {
                        foreach (Parameter p in element.Parameters)
                        {
                            if (p == null || p.Definition == null) continue;
                            string name = p.Definition.Name;
                            if (!string.IsNullOrWhiteSpace(name)) seen.Add(name);
                        }

                        Element type = doc.GetElement(element.GetTypeId());
                        if (type == null) continue;

                        foreach (Parameter p in type.Parameters)
                        {
                            if (p == null || p.Definition == null) continue;
                            string name = p.Definition.Name;
                            if (!string.IsNullOrWhiteSpace(name)) seen.Add(name);
                        }
                    }
                    catch
                    {
                        // Skip elements whose parameter set cannot be read.
                    }
                }

                names = seen.ToList();
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }

            return names;
        }
    }
}
