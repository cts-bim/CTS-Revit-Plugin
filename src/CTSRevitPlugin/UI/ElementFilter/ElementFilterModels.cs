using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace CTSRevitPlugin.UI.ElementFilter
{
    /// <summary>What a filter does to the elements it matches.</summary>
    public enum FilterAction
    {
        /// <summary>No default: the palette asks which action to run.</summary>
        Ask = 0,
        Select = 1,
        Isolate = 2,
        Hide = 3,
        Halftone = 4,
        Transparency = 5
    }

    /// <summary>How a condition compares the parameter to the value.</summary>
    public enum RuleOperator
    {
        Equals = 0,
        NotEquals = 1,
        Contains = 2,
        NotContains = 3,
        BeginsWith = 4,
        EndsWith = 5,
        Greater = 6,
        GreaterOrEqual = 7,
        Less = 8,
        LessOrEqual = 9,
        IsEmpty = 10,
        IsNotEmpty = 11
    }

    /// <summary>Where a condition's value comes from.</summary>
    public enum RuleInputType
    {
        /// <summary>The value stored with the rule.</summary>
        Fixed = 0,

        /// <summary>
        /// The palette asks for the value each time the filter runs, so one rule
        /// covers "Zone = whatever the user types now".
        /// </summary>
        Prompt = 1
    }

    /// <summary>How the conditions of a filter combine.</summary>
    public enum RuleMatch
    {
        /// <summary>AND.</summary>
        All = 0,

        /// <summary>OR.</summary>
        Any = 1
    }

    /// <summary>Which elements the filter looks at.</summary>
    public enum FilterScope
    {
        ActiveView = 0,
        EntireModel = 1
    }

    public sealed class FilterCondition
    {
        /// <summary>
        /// Revit parameter name. Matched case-insensitively here, unlike the eVolve
        /// tool, because case-sensitive names are a common source of silent misses.
        /// </summary>
        [XmlAttribute]
        public string ParameterName { get; set; } = "";

        [XmlAttribute]
        public RuleOperator Operator { get; set; } = RuleOperator.Equals;

        [XmlAttribute]
        public string Value { get; set; } = "";

        [XmlAttribute]
        public RuleInputType InputType { get; set; } = RuleInputType.Fixed;

        public FilterCondition Clone()
        {
            return new FilterCondition
            {
                ParameterName = ParameterName,
                Operator = Operator,
                Value = Value,
                InputType = InputType
            };
        }

        public override string ToString()
        {
            string name = string.IsNullOrWhiteSpace(ParameterName) ? "(parameter)" : ParameterName;

            if (Operator == RuleOperator.IsEmpty) return name + " is empty";
            if (Operator == RuleOperator.IsNotEmpty) return name + " is not empty";

            string value = InputType == RuleInputType.Prompt
                ? "(ask when run)"
                : "\"" + (Value ?? "") + "\"";

            return name + " " + OperatorText(Operator) + " " + value;
        }

        public static string OperatorText(RuleOperator op)
        {
            switch (op)
            {
                case RuleOperator.Equals: return "equals";
                case RuleOperator.NotEquals: return "does not equal";
                case RuleOperator.Contains: return "contains";
                case RuleOperator.NotContains: return "does not contain";
                case RuleOperator.BeginsWith: return "begins with";
                case RuleOperator.EndsWith: return "ends with";
                case RuleOperator.Greater: return ">";
                case RuleOperator.GreaterOrEqual: return ">=";
                case RuleOperator.Less: return "<";
                case RuleOperator.LessOrEqual: return "<=";
                case RuleOperator.IsEmpty: return "is empty";
                case RuleOperator.IsNotEmpty: return "is not empty";
                default: return "equals";
            }
        }
    }

    public sealed class FilterDefinition
    {
        [XmlAttribute]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [XmlAttribute]
        public string Name { get; set; } = "New filter";

        [XmlAttribute]
        public RuleMatch Match { get; set; } = RuleMatch.All;

        [XmlAttribute]
        public FilterScope Scope { get; set; } = FilterScope.ActiveView;

        /// <summary>Default action run on double-click. Ask means "show the menu".</summary>
        [XmlAttribute]
        public FilterAction DefaultAction { get; set; } = FilterAction.Ask;

        /// <summary>Surface transparency percentage used by the Transparency action.</summary>
        [XmlAttribute]
        public int Transparency { get; set; } = 70;

        /// <summary>
        /// BuiltInCategory ids the filter is limited to. Empty means every model
        /// category, which is slower but is the sane default for a new filter.
        /// </summary>
        public List<long> CategoryIds { get; set; } = new List<long>();

        /// <summary>Kept only so the designer can show what was picked without a document.</summary>
        public List<string> CategoryNames { get; set; } = new List<string>();

        public List<FilterCondition> Conditions { get; set; } = new List<FilterCondition>();

        public FilterDefinition Clone()
        {
            FilterDefinition copy = new FilterDefinition
            {
                Id = Id,
                Name = Name,
                Match = Match,
                Scope = Scope,
                DefaultAction = DefaultAction,
                Transparency = Transparency,
                CategoryIds = new List<long>(CategoryIds ?? new List<long>()),
                CategoryNames = new List<string>(CategoryNames ?? new List<string>())
            };

            foreach (FilterCondition condition in Conditions ?? new List<FilterCondition>())
                copy.Conditions.Add(condition.Clone());

            return copy;
        }

        public string Summary
        {
            get
            {
                int count = Conditions == null ? 0 : Conditions.Count;
                string rules = count == 1 ? "1 condition" : count + " conditions";
                string join = Match == RuleMatch.All ? "all" : "any";
                string where = Scope == FilterScope.ActiveView ? "active view" : "whole model";
                return rules + " (" + join + ") · " + where;
            }
        }

        public static string ActionText(FilterAction action)
        {
            switch (action)
            {
                case FilterAction.Select: return "Select";
                case FilterAction.Isolate: return "Isolate";
                case FilterAction.Hide: return "Hide";
                case FilterAction.Halftone: return "Halftone";
                case FilterAction.Transparency: return "Transparency";
                default: return "Ask every time";
            }
        }
    }

    [XmlRoot("CtsElementFilters")]
    public sealed class FilterLibrary
    {
        [XmlAttribute]
        public int Version { get; set; } = 1;

        [XmlAttribute]
        public string SelectedId { get; set; }

        public List<FilterDefinition> Filters { get; set; } = new List<FilterDefinition>();
    }
}
