using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace iD_Develops.Utilities
{
    public class ExcludePropertyContractResolver : DefaultContractResolver
    {
        private readonly HashSet<string> _propsToExclude;

        public ExcludePropertyContractResolver(IEnumerable<string> propNamesToExclude)
        {
            _propsToExclude = new HashSet<string>(propNamesToExclude);
        }

        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            var properties = base.CreateProperties(type, memberSerialization);

            // Only include properties whose name is not excluded (and handle null names safely)
            return properties
                .Where(p => p.PropertyName is string name && !_propsToExclude.Contains(name))
                .ToList();
        }

    }
}
