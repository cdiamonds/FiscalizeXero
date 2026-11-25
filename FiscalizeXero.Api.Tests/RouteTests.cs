using System.Reflection;
using FiscalizeXero.Api.Controllers.Fiscalization;
using FiscalizeXero.Api.Controllers.Xero;
using FiscalizeXero.Api.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    public class RouteTests
    {
        private static IEnumerable<Type> Controllers() =>
            typeof(FiscalizationController).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        [Fact]
        public void Every_company_data_controller_is_company_scoped()
        {
            var scoped = Controllers().Where(t => t.GetCustomAttribute<CompanyScopedAttribute>(inherit: true) != null).Select(t => t.Name).ToList();
            foreach (var name in new[]
                     {
                         nameof(InvoicesController), nameof(CreditNotesController), nameof(CustomersController), nameof(ItemsController),
                         nameof(TaxRatesController), nameof(OrganisationController), nameof(WebhooksController), nameof(FiscalizationController),
                         "ClientConfigsController"
                     })
                Assert.Contains(name, scoped);
        }

        [Fact]
        public void No_route_mentions_the_previous_product_or_its_document_types()
        {
            var routes = Controllers()
                .SelectMany(t => t.GetCustomAttributes<RouteAttribute>().Select(r => r.Template)
                    .Concat(t.GetMethods().SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(h => h.Template ?? ""))))
                .ToList();
            Assert.Contains("api/xero/invoices", routes);
            Assert.Contains("api/xero/creditnotes", routes);
            Assert.DoesNotContain(routes, r => r.Contains("salesreceipt", StringComparison.OrdinalIgnoreCase) || r.Contains("refundreceipt", StringComparison.OrdinalIgnoreCase)
                                               || r.Contains("creditmemo", StringComparison.OrdinalIgnoreCase));
        }

        [Theory]
        [InlineData("invoices", true)]
        [InlineData("creditnotes", true)]
        [InlineData("salesreceipts", false)]
        [InlineData("invoicesx", false)]
        public void The_shared_attachment_and_failure_routes_only_accept_the_two_document_types(string segment, bool matches)
        {
            var template = typeof(FiscalizationController).GetMethod(nameof(FiscalizationController.GetFailure))!.GetCustomAttribute<HttpGetAttribute>()!.Template!;
            var pattern = RoutePatternFactory.Parse(template);
            var policy = pattern.GetParameter("documentType")!.ParameterPolicies.Single().Content!;
            Assert.StartsWith("regex(", policy);
            var regex = policy["regex(".Length..^1];
            Assert.Equal(matches, System.Text.RegularExpressions.Regex.IsMatch(segment, regex));
            Assert.Equal("guid", pattern.GetParameter("id")!.ParameterPolicies.Single().Content);
        }

        [Fact]
        public void Document_ids_in_routes_are_guids()
        {
            var templates = new[] { typeof(FiscalizationController), typeof(InvoicesController), typeof(CreditNotesController), typeof(CustomersController) }
                .SelectMany(t => t.GetMethods().SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(h => h.Template ?? "")))
                .Where(t => t.Contains("{id"));
            Assert.All(templates, t => Assert.Contains("{id:guid}", t));
        }
    }
}
