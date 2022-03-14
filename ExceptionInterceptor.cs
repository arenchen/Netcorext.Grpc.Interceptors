using System.Reflection;
using FluentValidation;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Netcorext.Contracts.Abstractions;

namespace Netcorext.Grpc.Interceptors;

public class ExceptionInterceptor : Interceptor
{
    private readonly ILogger<ExceptionInterceptor> _logger;

    public ExceptionInterceptor(ILogger<ExceptionInterceptor> logger)
    {
        _logger = logger;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request,
                                                                                  ServerCallContext context,
                                                                                  UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (Exception ex)
        {
            LogError(ex);

            return GetErrorResponse<TResponse>(ex);
        }
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream,
                                                                                            ServerCallContext context,
                                                                                            ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(requestStream, context);
        }
        catch (Exception ex)
        {
            LogError(ex);

            return GetErrorResponse<TResponse>(ex);
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request,
                                                                                 IServerStreamWriter<TResponse> responseStream,
                                                                                 ServerCallContext context,
                                                                                 ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await continuation(request, responseStream, context);
        }
        catch (Exception ex)
        {
            LogError(ex);

            var response = GetErrorResponse<TResponse>(ex);

            await responseStream.WriteAsync(response);
        }
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream,
                                                                                 IServerStreamWriter<TResponse> responseStream,
                                                                                 ServerCallContext context,
                                                                                 DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await continuation(requestStream, responseStream, context);
        }
        catch (Exception ex)
        {
            LogError(ex);

            var response = GetErrorResponse<TResponse>(ex);

            await responseStream.WriteAsync(response);
        }
    }

    private void LogError(Exception ex)
    {
        _logger.LogError(ex, ex.ToString());
    }

    private static TResponse GetErrorResponse<TResponse>(Exception ex)
    {
        string? code;
        string? message;

        switch (ex)
        {
            case ValidationException validationEx:
                var failure = validationEx.Errors.First();

                code = typeof(ResultCode).GetFields(BindingFlags.Public | BindingFlags.Static)
                                         .Select(t => t.GetValue(null)?.ToString())
                                         .FirstOrDefault(t => t == failure.ErrorCode);

                code ??= ResultCode.InvalidInput;

                message = failure.ErrorMessage;

                break;
            case ArgumentException argumentEx:
                code = ResultCode.InvalidInput;
                message = argumentEx.Message;

                break;
            case BadHttpRequestException badHttpRequestEx:
                code = badHttpRequestEx.Message == "Request body too large."
                           ? ResultCode.PayloadTooLarge
                           : ResultCode.InvalidInput;

                message = badHttpRequestEx.Message;

                break;
            default:
                code = ResultCode.InternalServerError;
                message = ex.Message;

                break;
        }

        var response = Activator.CreateInstance<TResponse>();
        var type = typeof(TResponse);
        type.GetProperty("Code")?.SetValue(response, code);
        type.GetProperty("Message")?.SetValue(response, message);

        return response;
    }
}