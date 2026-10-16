using System.Security.Cryptography;

namespace CoreBancario.Api.Datos;

/// <summary>Error controlado del almacen (llave ausente, archivo no encontrado, etc.).</summary>
public class AlmacenDocumentosException : Exception
{
    public string Codigo { get; }
    public AlmacenDocumentosException(string codigo, string mensaje) : base(mensaje) { Codigo = codigo; }
}

/// <summary>
/// Almacen de archivos detras de una interfaz: la aplicacion solo guarda, lee y elimina por una llave opaca.
/// La primera implementacion es disco local cifrado; un gestor documental externo la sustituye sin tocar el resto.
/// </summary>
public interface IAlmacenDocumentos
{
    /// <summary>Guarda el contenido (cifrado) y devuelve la ruta relativa dentro del almacen y el id de la llave usada.</summary>
    Task<(string Ruta, string IdLlave)> GuardarAsync(string directorioBase, long idCompania, byte[] contenido);
    Task<byte[]> LeerAsync(string directorioBase, string ruta, string idLlave);
    Task EliminarAsync(string directorioBase, string ruta);
}

/// <summary>
/// Disco local con cifrado AES-256-GCM a nivel de aplicacion. Formato del archivo: nonce (12) + tag (16) + datos cifrados.
/// La llave NO vive en la base ni en el directorio: se configura en Documentos:Llaves:{id} (base64 de 32 bytes), por variable de
/// entorno o secreto del servidor; Documentos:IdLlave indica cual se usa para guardar y cada documento recuerda la suya (permite rotar).
/// Solo en Development, si no hay llave configurada, se genera una de pruebas en dev_documentos.key (nunca se hace en produccion).
/// </summary>
public sealed class AlmacenDiscoCifrado : IAlmacenDocumentos
{
    private const int TamNonce = 12;
    private const int TamTag = 16;
    private static readonly object CandadoLlaveDev = new();

    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;

    public AlmacenDiscoCifrado(IConfiguration config, IHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    private string IdLlaveActual => string.IsNullOrWhiteSpace(_config["Documentos:IdLlave"]) ? "k1" : _config["Documentos:IdLlave"]!.Trim();

    private byte[] ObtenerLlave(string id)
    {
        var b64 = _config["Documentos:Llaves:" + id];
        if (string.IsNullOrWhiteSpace(b64) && _env.IsDevelopment() && id == "k1") b64 = LlaveDesarrollo();
        if (string.IsNullOrWhiteSpace(b64))
            throw new AlmacenDocumentosException("documentos_llave_no_configurada",
                $"Falta la llave de cifrado de documentos '{id}'. Configure Documentos:Llaves:{id} (base64 de 32 bytes) como variable de entorno o secreto del servidor.");
        byte[] llave;
        try { llave = Convert.FromBase64String(b64.Trim()); }
        catch (FormatException) { throw new AlmacenDocumentosException("documentos_llave_invalida", $"La llave de documentos '{id}' no es base64 valido."); }
        if (llave.Length != 32)
            throw new AlmacenDocumentosException("documentos_llave_invalida", $"La llave de documentos '{id}' debe tener 32 bytes (AES-256).");
        return llave;
    }

    private string LlaveDesarrollo()
    {
        lock (CandadoLlaveDev)
        {
            var ruta = Path.Combine(_env.ContentRootPath, "dev_documentos.key");
            if (File.Exists(ruta)) return File.ReadAllText(ruta).Trim();
            var nueva = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            File.WriteAllText(ruta, nueva);
            return nueva;
        }
    }

    private string Raiz(string directorioBase)
    {
        var d = string.IsNullOrWhiteSpace(directorioBase) ? "almacen_documentos" : directorioBase.Trim();
        return Path.GetFullPath(Path.IsPathRooted(d) ? d : Path.Combine(_env.ContentRootPath, d));
    }

    /// <summary>Ruta completa dentro de la raiz; rechaza cualquier intento de salirse de ella.</summary>
    private string Completa(string directorioBase, string ruta)
    {
        var raiz = Raiz(directorioBase);
        var completa = Path.GetFullPath(Path.Combine(raiz, ruta));
        if (!completa.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new AlmacenDocumentosException("documentos_ruta_invalida", "La ruta del documento no es valida.");
        return completa;
    }

    public async Task<(string Ruta, string IdLlave)> GuardarAsync(string directorioBase, long idCompania, byte[] contenido)
    {
        var idLlave = IdLlaveActual;
        var llave = ObtenerLlave(idLlave);
        var nonce = RandomNumberGenerator.GetBytes(TamNonce);
        var cifrado = new byte[contenido.Length];
        var tag = new byte[TamTag];
        using (var aes = new AesGcm(llave, TamTag)) aes.Encrypt(nonce, contenido, cifrado, tag);

        var salida = new byte[TamNonce + TamTag + cifrado.Length];
        Buffer.BlockCopy(nonce, 0, salida, 0, TamNonce);
        Buffer.BlockCopy(tag, 0, salida, TamNonce, TamTag);
        Buffer.BlockCopy(cifrado, 0, salida, TamNonce + TamTag, cifrado.Length);

        var relativa = $"{idCompania}/{DateTime.UtcNow:yyyy}/{Guid.NewGuid():N}.bin";
        var completa = Completa(directorioBase, relativa);
        Directory.CreateDirectory(Path.GetDirectoryName(completa)!);
        await File.WriteAllBytesAsync(completa, salida);
        return (relativa, idLlave);
    }

    public async Task<byte[]> LeerAsync(string directorioBase, string ruta, string idLlave)
    {
        var completa = Completa(directorioBase, ruta);
        if (!File.Exists(completa))
            throw new AlmacenDocumentosException("documento_archivo_no_encontrado", "El archivo del documento no se encuentra en el almacen.");
        var datos = await File.ReadAllBytesAsync(completa);
        if (datos.Length < TamNonce + TamTag)
            throw new AlmacenDocumentosException("documento_archivo_corrupto", "El archivo del documento esta incompleto.");

        return Descifrar(datos, ObtenerLlave(idLlave));
    }

    // Sincrono a proposito: los Span no pueden vivir en metodos async.
    private static byte[] Descifrar(byte[] datos, byte[] llave)
    {
        var nonce = datos.AsSpan(0, TamNonce);
        var tag = datos.AsSpan(TamNonce, TamTag);
        var cifrado = datos.AsSpan(TamNonce + TamTag);
        var claro = new byte[cifrado.Length];
        try
        {
            using var aes = new AesGcm(llave, TamTag);
            aes.Decrypt(nonce, cifrado, tag, claro);
        }
        catch (CryptographicException)
        {
            throw new AlmacenDocumentosException("documento_archivo_corrupto", "No se pudo descifrar el documento (archivo alterado o llave incorrecta).");
        }
        return claro;
    }

    public Task EliminarAsync(string directorioBase, string ruta)
    {
        var completa = Completa(directorioBase, ruta);
        if (File.Exists(completa)) File.Delete(completa);
        return Task.CompletedTask;
    }
}
