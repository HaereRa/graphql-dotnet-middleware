using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

using Newtonsoft.Json;

using GraphQL.Middleware.Services;
using GraphQL.Middleware.ViewModels;
using GraphQL.Http;

namespace GraphQL.Middleware
{
    public class GraphQLMiddleware
    {
        public readonly RequestDelegate _next;
        private readonly IServiceProvider _serviceProvider;
        private readonly GraphQLMiddlewareOptions _options;

        public GraphQLMiddleware(RequestDelegate next, IServiceProvider serviceProvider, IOptions<GraphQLMiddlewareOptions> options)
        {
            _next = next;
            _serviceProvider = serviceProvider;
            _options = options.Value ?? new GraphQLMiddlewareOptions { RequestPath = "/GraphQL" };
        }

        public async Task Invoke(HttpContext context)
        {
            if (context.Request.Method != "POST" || !context.Request.Path.Value.Equals(_options.RequestPath, StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            var tokenSource = new CancellationTokenSource();
            var cancellationToken = tokenSource.Token;
            cancellationToken.ThrowIfCancellationRequested();

            var graphQLService = _serviceProvider.GetService<IGraphQLService>();

            // Read query parameters from body of request
            var query = ReadBody<GraphQLQuery>(context.Request.Body);

            // Execute query
            var result = await graphQLService.ExecuteQueryAsync(query.Query, query.Variables, query.OperationName, context.User, cancellationToken);

            var docuemntwriter = new DocumentWriter(true);

            var response = context.Response;

            response.ContentType = "application/json";
            // Add content-type utf8 here?

            // Set the status code based on the outcome of the result
            response.StatusCode = (result.Errors?.Count > 0)
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status200OK;

            // Create a text writer much like ASP.Net's MVC ContentResultExecutor
            using (var textWriter = new HttpResponseStreamWriter(response.Body, Encoding.UTF8, 16 * 1024))
            {
                var content = docuemntwriter.Write(result);

                response.ContentLength = Encoding.UTF8.GetByteCount(content);

                await textWriter.WriteAsync(content);

                await textWriter.FlushAsync();
            }
        }

        /// <summary>
        /// Read the input body and deserialise from JSON to <typeparamref name="TObject"/>
        /// </summary>
        /// <typeparam name="TObject"></typeparam>
        /// <param name="input"></param>
        /// <returns></returns>
        private TObject ReadBody<TObject>(Stream input)
        {
            // TODO: We're assuming UTF-8 encoding here...
            using (var reader = new JsonTextReader(new StreamReader(input, Encoding.UTF8)))
            {
                reader.CloseInput = false;
                var jsonSerialiser = JsonSerializer.Create();
                return jsonSerialiser.Deserialize<TObject>(reader);
            }
        }
    }
}
