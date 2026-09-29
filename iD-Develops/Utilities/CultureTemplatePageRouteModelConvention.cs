using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace iD_Develops.Utilities
{
    /// <summary>
    /// Adds a culture prefix (e.g. /en-us/...) to Razor Pages routes.
    /// This enables URLs like /en-us/ (Index) and /en-us/identity/account/login (Identity UI),
    /// while avoiding double-appending when a route already contains {culture}.
    /// </summary>
    public sealed class CultureTemplatePageRouteModelConvention : IPageRouteModelConvention
    {
        public void Apply(PageRouteModel model)
        {
            if (model?.Selectors == null || model.Selectors.Count == 0)
                return;

            foreach (var selector in model.Selectors)
            {
                // Some selectors may not be attribute-routed.
                if (selector.AttributeRouteModel == null)
                    continue;

                var originalTemplate = selector.AttributeRouteModel.Template ?? string.Empty;

                // Only append the culture if it is not already part of the URL
                if (originalTemplate.Contains("{culture", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Use lowercase default to match your URL scheme (/en-us, /nl-nl)
                var newTemplate = AttributeRouteModel.CombineTemplates("{culture=en-us}", originalTemplate);
                selector.AttributeRouteModel.Template = newTemplate;
            }
        }
    }
}
