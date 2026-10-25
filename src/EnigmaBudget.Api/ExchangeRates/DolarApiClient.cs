using System.Globalization;
using System.Net;
using System.Text.Json;

namespace EnigmaBudget.Api.ExchangeRates;

public sealed record DolarApiQuote(string Currency, string House, string Name, decimal? Buy, decimal? Sell, DateTimeOffset ProviderUpdatedAt);

public sealed class DolarApiClient(HttpClient httpClient)
{
    public const string Endpoint = "https://dolarapi.com/v1/dolares";
    public const int MaxResponseBytes = 256 * 1024;
    private const decimal MaxStoredRate = 9999999999.99999999m;

    public async Task<IReadOnlyList<DolarApiQuote>> GetDollarsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("DolarAPI no devolvió una respuesta válida.");
        if (response.Content.Headers.ContentLength is > MaxResponseBytes) throw new InvalidDataException("La respuesta de DolarAPI supera el tamaño permitido.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaxResponseBytes) throw new InvalidDataException("La respuesta de DolarAPI supera el tamaño permitido.");
            buffer.Write(chunk, 0, read);
        }
        return Parse(buffer.ToArray());
    }

    public static IReadOnlyList<DolarApiQuote> Parse(ReadOnlySpan<byte> json)
    {
        using var document = JsonDocument.Parse(json.ToArray());
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            throw new InvalidDataException("DolarAPI no devolvió cotizaciones utilizables.");
        var result = new List<DolarApiQuote>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Una cotización de DolarAPI no es válida.");
            var currency = RequiredString(item, "moneda");
            var house = RequiredString(item, "casa");
            var name = RequiredString(item, "nombre");
            if (!string.Equals(currency, "USD", StringComparison.Ordinal)) throw new InvalidDataException("DolarAPI devolvió una moneda distinta de USD.");
            if (house.Length > 80 || name.Length > 120) throw new InvalidDataException("Una cotización de DolarAPI supera el tamaño permitido.");
            var buy = ReadRate(item, "compra");
            var sell = ReadRate(item, "venta");
            if (buy is null && sell is null) throw new InvalidDataException("La cotización no contiene un lado de conversión positivo.");
            var dateText = RequiredString(item, "fechaActualizacion");
            if (!DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var updated))
                throw new InvalidDataException("La fecha de actualización de DolarAPI no es válida.");
            result.Add(new(currency, house, name, buy, sell, updated));
        }
        return result;
    }

    private static string RequiredString(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException("Una cotización de DolarAPI tiene campos incompletos.");
        return value.GetString()!.Trim();
    }

    private static decimal? ReadRate(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value)) throw new InvalidDataException("Una cotización de DolarAPI tiene campos incompletos.");
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var rate) || rate <= 0m || rate > MaxStoredRate || decimal.Round(rate, 8) != rate)
            throw new InvalidDataException("Una cotización de DolarAPI contiene un precio inválido.");
        return rate;
    }
}

public static class ExchangeRateMath
{
    public static decimal Convert(decimal amount, string sourceCurrency, string targetCurrency, decimal rate, string side)
    {
        if (rate <= 0m || side is not ("Compra" or "Venta")) throw new ArgumentException("La cotización y el lado de conversión deben ser válidos.");
        if (sourceCurrency == targetCurrency && sourceCurrency is "ARS" or "USD") return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (sourceCurrency == "USD" && targetCurrency == "ARS") return decimal.Round(amount * rate, 2, MidpointRounding.AwayFromZero);
        if (sourceCurrency == "ARS" && targetCurrency == "USD") return decimal.Round(amount / rate, 2, MidpointRounding.AwayFromZero);
        throw new ArgumentException("Solo se puede convertir entre ARS y USD.");
    }
}
