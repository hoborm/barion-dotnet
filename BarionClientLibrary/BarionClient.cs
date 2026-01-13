using System;
using System.Configuration;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BarionClientLibrary.Helpers;
using BarionClientLibrary.Operations;
using BarionClientLibrary.Operations.Common;
using BarionClientLibrary.RetryPolicies;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BarionClientLibrary;

/// <summary>
/// Provides a base class for executing Barion operations.
/// </summary>
public class BarionClient : IDisposable
{
    private HttpClient _httpClient;
    private readonly BarionSettings _settings;
    private IRetryPolicy _retryPolicy;
    private TimeSpan _timeout;
    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan _maxTimeout = TimeSpan.FromMilliseconds(int.MaxValue);
    private static readonly TimeSpan _infiniteTimeout = System.Threading.Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Initializes a new instance of the BarionClientLibrary.BarionClient class.
    /// </summary>
    /// <param name="settings">Barion specific settings.</param>
    public BarionClient(BarionSettings settings) : this(settings, new HttpClient()) { }

    /// <summary>
    /// Initializes a new instance of the BarionClientLibrary.BarionClient class.
    /// </summary>
    /// <param name="settings">Barion specific settings.</param>
    /// <param name="httpClient">HttpClient instance to use for sending HTTP requests.</param>
    public BarionClient(BarionSettings settings, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;

        ArgumentNullException.ThrowIfNull(settings);

        if (settings.BaseUrl == null)
        {
            throw new ConfigurationErrorsException(nameof(settings.BaseUrl)); //TODO: Check if this Error type is correct.
        }

        if (!settings.BaseUrl.IsAbsoluteUri)
        {
            throw new ConfigurationErrorsException($"BaseUrl must be an absolute Uri. Actual value: {settings.BaseUrl}"); //TODO: Check if this Error type is correct.
        }

        _settings = settings;

        _retryPolicy = new ExponentialRetry();

        _timeout = _defaultTimeout;
    }

    /// <summary>
    /// Gets or sets the retry policy to use on transient failures.
    /// </summary>
    public IRetryPolicy RetryPolicy
    {
        get => _retryPolicy;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            _retryPolicy = value;
        }
    }

    /// <summary>
    /// Gets or sets the number of milliseconds to wait before the request times out.
    /// </summary>
    public TimeSpan Timeout
    {
        get => _timeout;
        set
        {
            if (value != _infiniteTimeout && (value <= TimeSpan.Zero || value > _maxTimeout))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _timeout = value;
        }
    }

    /// <summary>
    /// Executes a Barion operation.
    /// </summary>
    /// <typeparam name="TResult">The type of the result of the Barion operation.</typeparam>
    /// <param name="operation">The Barion operation to execute.</param>
    /// <returns>Returns System.Threading.Tasks.Task`1.The task object representing the asynchronous operation.</returns>
    public async Task<TResult> ExecuteAsync<TResult>(BarionOperation operation)
        where TResult : BarionOperationResult
    {
        return typeof(TResult) != operation.ResultType
            ? throw new InvalidOperationException("TResult should be equal to the ResultType of the operation.")
            : await ExecuteAsync(operation).ConfigureAwait(false) as TResult;
    }

    /// <summary>
    /// Executes a Barion operation.
    /// </summary>
    /// <param name="operation">The Barion operation to execute.</param>
    /// <returns>Returns System.Threading.Tasks.Task`1.The task object representing the asynchronous operation.</returns>
    public async Task<BarionOperationResult> ExecuteAsync(BarionOperation operation)
    {
        return await ExecuteAsync(operation, default).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a Barion operation.
    /// </summary>
    /// <typeparam name="TResult">The type of the result of the Barion operation.</typeparam>
    /// <param name="operation">The Barion operation to execute.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns>Returns System.Threading.Tasks.Task`1.The task object representing the asynchronous operation.</returns>
    public async Task<TResult> ExecuteAsync<TResult>(BarionOperation operation, CancellationToken cancellationToken)
        where TResult : BarionOperationResult
    {
        return typeof(TResult) != operation.ResultType
            ? throw new InvalidOperationException("TResult should be equal to the ResultType of the operation.")
            : await ExecuteAsync(operation, cancellationToken).ConfigureAwait(false) as TResult;
    }

    /// <summary>
    /// Executes a Barion operation.
    /// </summary>
    /// <param name="operation">The Barion operation to execute.</param>
    /// <param name="cancellationToken">The cancellation token to cancel operation.</param>
    /// <returns>Returns System.Threading.Tasks.Task`1.The task object representing the asynchronous operation.</returns>
    public async Task<BarionOperationResult> ExecuteAsync(BarionOperation operation, CancellationToken cancellationToken)
    {
        CheckDisposed();
        ValidateOperation(operation);

        operation.POSKey = _settings.POSKey;

        return await SendWithRetry(operation, cancellationToken).ConfigureAwait(false);
    }

    private async Task<BarionOperationResult> SendWithRetry(BarionOperation operation, CancellationToken cancellationToken)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SetTimeout(linkedCts);

        var shouldRetry = false;
        uint currentRetryCount = 0;
        var retryInterval = TimeSpan.Zero;
        BarionOperationResult result = null;

        do
        {
            var message = PrepareHttpRequestMessage(operation);

            try
            {
                var responseMessage = await _httpClient.SendAsync(message, linkedCts.Token).ConfigureAwait(false);

                result = await CreateResultFromResponseMessage(responseMessage, operation).ConfigureAwait(false);

                if (!result.IsOperationSuccessful)
                {
                    shouldRetry = _retryPolicy.CreateInstance().ShouldRetry(currentRetryCount, responseMessage.StatusCode, out retryInterval);
                }
            }
            catch (Exception ex)
            {
                shouldRetry = _retryPolicy.CreateInstance().ShouldRetry(currentRetryCount, ex, out retryInterval);

                if (!shouldRetry)
                {
                    throw;
                }
            }

            if (shouldRetry)
            {
                await Task.Delay(retryInterval, cancellationToken).ConfigureAwait(false);
                currentRetryCount++;
            }
        } while (shouldRetry && !linkedCts.IsCancellationRequested);

        return result;
    }

    private HttpRequestMessage PrepareHttpRequestMessage(BarionOperation operation)
    {
        var message = new HttpRequestMessage(operation.Method, new Uri(_settings.BaseUrl, operation.RelativeUri));

        if (operation.Method == HttpMethod.Post || operation.Method == HttpMethod.Put)
        {
            var body = JsonConvert.SerializeObject(operation, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                Converters = [new StringEnumConverter(), new CultureInfoJsonConverter()]
            });
            message.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return message;
    }

    private static async Task<BarionOperationResult> CreateResultFromResponseMessage(HttpResponseMessage responseMessage, BarionOperation operation)
    {
        var response = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        var operationResult = (BarionOperationResult)JsonConvert.DeserializeObject(response, operation.ResultType, new JsonSerializerSettings
        {
            Converters = [new StringEnumConverter { AllowIntegerValues = false }, new CultureInfoJsonConverter()]
        });

        if (operationResult == null)
        {
            return CreateFailedOperationResult(operation.ResultType, "Deserialized result was null");
        }

        if (!responseMessage.IsSuccessStatusCode && operationResult.Errors == null)
        {
            return CreateFailedOperationResult(operation.ResultType, responseMessage.StatusCode.ToString(), responseMessage.ReasonPhrase, response);
        }

        operationResult.IsOperationSuccessful = responseMessage.IsSuccessStatusCode && (operationResult.Errors == null || operationResult.Errors.Length == 0);

        return operationResult;
    }

    private void SetTimeout(CancellationTokenSource cancellationTokenSource)
    {
        if (_timeout != _infiniteTimeout)
        {
            cancellationTokenSource.CancelAfter(_timeout);
        }
    }

    private static void ValidateOperation(BarionOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.RelativeUri == null)
        {
            throw new InvalidOperationException(nameof(operation.RelativeUri));//TODO: Check if this Error type is correct.
        }

        if (operation.RelativeUri.IsAbsoluteUri)
        {
            throw new InvalidOperationException($"operation.RelativeUri should be a relative Uri. {nameof(operation.RelativeUri)}");//TODO: Check if this Error type is correct.
        }

        if (operation.ResultType == null)
        {
            throw new InvalidOperationException(nameof(operation.ResultType));//TODO: Check if this Error type is correct.
        }

        if (!operation.ResultType.GetTypeInfo().IsSubclassOf(typeof(BarionOperationResult)))
        {
            throw new InvalidOperationException($"operation.RelativeUri should be a relative Uri. {nameof(operation.ResultType)}");//TODO: Check if this Error type is correct.
        }

        if (operation.Method == null)
        {
            throw new InvalidOperationException(nameof(operation.Method));//TODO: Check if this Error type is correct.
        }
    }

    private static BarionOperationResult CreateFailedOperationResult(Type resultType, string errorCode, string title = null, string description = null)
    {
        var result = Activator.CreateInstance(resultType) as BarionOperationResult;
        result.IsOperationSuccessful = false;
        result.Errors =
        [
            new Error
            {
                ErrorCode = errorCode,
                Title = title,
                Description = description
            }
        ];

        return result;
    }

    #region IDisposable members

    private volatile bool _disposed;

    /// <summary>
    /// Releases the unmanaged resources and disposes of the managed resources used by the BarionClient.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~BarionClient()
    {
        Dispose(false);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the BarionClient and optionally disposes of the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to releases only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;

            if (_httpClient != null)
            {
                _httpClient.Dispose();
                _httpClient = null;
            }
        }
    }

    #endregion

    private void CheckDisposed()
    {
        if (!_disposed)
        {
            return;
        }
        throw new ObjectDisposedException(GetType().ToString());
    }
}
