using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Sahada.Api.Common;

/// <summary>The landing text of the API reference.</summary>
internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Sahada",
            Version = "1.0",
            Description = """
                Field service management: customers with their sites and equipment, work orders with deadlines set by
                priority, a dispatcher's board that suggests who should go, and a technician app that keeps working
                without signal and syncs when it can.

                **Getting started**

                1. Create an account at `POST /api/auth/register` and paste the access token into *Authentication*.
                2. Create a company at `POST /api/companies`, then a customer with a site.
                3. Open a work order for the site and schedule it for a technician.

                Teams have three roles: technicians do the work on site, dispatchers also take in and schedule jobs,
                owners also manage the team.

                **Errors** follow RFC 9457 problem details with a stable `code`, for example `validation_failed`
                or `invalid_state`. Every response carries an `X-Correlation-Id`.
                """,
            License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://github.com/Beratoguzhan620/sahada/blob/main/LICENSE") },
        };
        return Task.CompletedTask;
    }
}
