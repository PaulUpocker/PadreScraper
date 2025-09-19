namespace PadreScraper;

using System.Text.Json.Serialization;

// Главный класс-обертка, который соответствует всей записи в IndexedDB
public class FirebaseUserRecord
{
    [JsonPropertyName("fbase_key")]
    public string? FbaseKey { get; set; }

    [JsonPropertyName("value")]
    public UserValue? Value { get; set; }
}

// Класс для внутреннего объекта "value"
public class UserValue
{
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("appName")]
    public string? AppName { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    // email может быть undefined, поэтому делаем его nullable
    [JsonPropertyName("email")]
    public string? Email { get; set; } 

    [JsonPropertyName("emailVerified")]
    public bool EmailVerified { get; set; }

    [JsonPropertyName("isAnonymous")]
    public bool IsAnonymous { get; set; }

    [JsonPropertyName("lastLoginAt")]
    public string? LastLoginAt { get; set; }

    // phoneNumber может быть undefined
    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; set; }

    // photoURL может быть undefined
    [JsonPropertyName("photoURL")]
    public string? PhotoURL { get; set; }
    
    // providerData это массив, но на скриншоте он пуст.
    // Мы можем определить его как массив объектов, если там могут быть данные.
    [JsonPropertyName("providerData")]
    public List<ProviderDataItem>? ProviderData { get; set; }

    [JsonPropertyName("stsTokenManager")]
    public StsTokenManager? StsTokenManager { get; set; }

    // tenantId может быть undefined
    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("uid")]
    public string? Uid { get; set; }

    // _redirectEventId может быть undefined
    [JsonPropertyName("_redirectEventId")]
    public string? RedirectEventId { get; set; }
}

// Класс для объекта "stsTokenManager", где лежат токены
public class StsTokenManager
{
    // На вашем скриншоте accessToken отсутствует, но refreshToken есть.
    // Часто бывает, что accessToken живет очень недолго и его нужно получать с помощью refreshToken.
    // Я добавлю оба поля на случай, если accessToken появляется позже.
    
    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expirationTime")]
    public long ExpirationTime { get; set; }
}

// Класс для элементов в массиве providerData.
// На скриншоте он пуст, но это типичная структура для Firebase.
public class ProviderDataItem
{
    [JsonPropertyName("providerId")]
    public string? ProviderId { get; set; }

    [JsonPropertyName("uid")]
    public string? Uid { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("photoURL")]
    public string? PhotoURL { get; set; }
}