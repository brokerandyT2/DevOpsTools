using System;
using System.Collections.Generic;

namespace x3squaredcircles.MobileAdapter.Generator.Discovery
{
    public class DiscoveredClass
    {
        public string Name { get; set; }
        public string FullName { get; set; }
        public string Namespace { get; set; }
        public string FilePath { get; set; }
        public DiscoveryMethod DiscoveryMethod { get; set; }
        public string DiscoverySource { get; set; }
        public List<DiscoveredProperty> Properties { get; set; } = new List<DiscoveredProperty>();
        public List<DiscoveredMethod> Methods { get; set; } = new List<DiscoveredMethod>();
        public List<string> Attributes { get; set; } = new List<string>();
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
        public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    }

    public class DiscoveredProperty
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool IsNullable { get; set; }
        public bool IsCollection { get; set; }
        public string CollectionElementType { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsPublic { get; set; }
        public List<string> Attributes { get; set; } = new List<string>();
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class DiscoveredMethod
    {
        public string Name { get; set; }
        public string ReturnType { get; set; }
        public bool IsAsync { get; set; }
        public bool IsPublic { get; set; }
        public List<DiscoveredParameter> Parameters { get; set; } = new List<DiscoveredParameter>();
        public List<string> Attributes { get; set; } = new List<string>();
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class DiscoveredParameter
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool IsNullable { get; set; }
        public bool HasDefaultValue { get; set; }
        public object DefaultValue { get; set; }
    }

    public enum DiscoveryMethod
    {
        Attribute,
        Pattern,
        Namespace,
        FilePath
    }

    public class DiscoveryConflict
    {
        public string ClassName { get; set; }
        public string FullName { get; set; }
        public List<DiscoveryMethodSource> ConflictingSources { get; set; } = new List<DiscoveryMethodSource>();
    }

    public class DiscoveryMethodSource
    {
        public DiscoveryMethod Method { get; set; }
        public string Source { get; set; }
        public string FilePath { get; set; }
    }
}