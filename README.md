# DocMCPFlowCSharp

CLI en C# (.NET 9) para orquestar la generación de libros/manuscritos usando modelos de OpenAI. Gestiona prompts, escribe el manuscrito en Markdown y registra la sesión en archivos con marca temporal.

## Requisitos

- .NET SDK 9.0 o superior
- Una clave de API de OpenAI válida

## Instalación y configuración

1) Clonar el repositorio y posicionarte en la carpeta del proyecto.

2) Configurar variables de entorno (recomendado):

   - Opción A (plantilla local):
     ```bash
     cp configurar_entorno.example.sh configurar_entorno.sh
     # Edita configurar_entorno.sh y coloca tu clave real
     source configurar_entorno.sh
     ```

   - Opción B (exports manuales):
     ```bash
     export OPENAI_API_KEY="sk-..."
     export OPENAI_MODEL="gpt-5-mini"        # opcional
     export OPENAI_MAX_COMPLETION_TOKENS=4096 # opcional
     export OPENAI_HTTP_TIMEOUT_SECONDS=300   # opcional
     export DRY_RUN=true                      # opcional
     export SHOW_USAGE=true                   # opcional
     export TREAT_REFUSAL_AS_ERROR=true       # opcional
     export DEMO_MODE=true                    # opcional (índice 2×2 por defecto)
     export CONTENT_CALLS_LIMIT=8             # opcional (máx. llamadas de contenido)
     ```

3) Compilar y ejecutar:
   ```bash
   dotnet build
   dotnet run
   ```

## Variables de entorno

- `OPENAI_API_KEY`: clave de OpenAI. Obligatoria.
- `OPENAI_MODEL`: modelo a usar, por defecto `gpt-5-mini`.
- `OPENAI_MAX_COMPLETION_TOKENS`: límite por llamada (defecto: 4096).
- `OPENAI_HTTP_TIMEOUT_SECONDS`: timeout HTTP en segundos (defecto: 300).
- `DRY_RUN`: si `true`, evita generar capítulos completos (sólo flujo). Defecto: `true` en plantilla.
- `SHOW_USAGE`: si `true`, muestra uso de tokens al finalizar (no disponible con la lib actual; se avisa). Defecto: `true`.
- `TREAT_REFUSAL_AS_ERROR`: si `true`, trata una negativa del modelo como error fatal. Defecto: `true`.
- `DEMO_MODE`: si `true`, limita el índice a 2 capítulos con 2 subcapítulos cada uno para pruebas. Defecto: `true`.
- `CONTENT_CALLS_LIMIT`: número máximo de llamadas a IA para generar contenido de subcapítulos. Defecto: `8`.

Estas opciones se leen en `Implementations/EnvironmentConfiguration.cs` y en `Program.cs`.

## Ejecución

Al ejecutar, el programa:

1) Verifica `OPENAI_API_KEY`. Si falta, muestra un aviso y termina.
2) Crea un archivo de log con timestamp en la raíz (por ejemplo `2025-08-29-19-00-30-log.txt`).
3) Inicializa el cliente OpenAI (`Implementations/OpenAiSdkLlmClient.cs`).
4) Orquesta la generación con `Orchestration/BookGenerator.cs` y escribe el manuscrito (`Implementations/MarkdownManuscriptWriter.cs`).

## Estructura del proyecto (resumen)

- `Program.cs`: punto de entrada y wiring principal.
- `Interfaces/`: contratos (`ILlmClient`, `IUserInteraction`, `IConfiguration`, etc.).
- `Implementations/`: implementaciones de interfaces (cliente OpenAI, UI consola, writer Markdown, configuración por entorno).
- `Orchestration/`: lógica de orquestación para generar el libro.
- `Utilities/`: utilidades (prompts, parseo de respuestas, etc.).
- `configurar_entorno.example.sh`: plantilla de entorno sin credenciales.
- `configurar_entorno.sh`: script local (ignorado por git) con tus credenciales reales.

## Autenticación

La autenticación se realiza mediante la variable `OPENAI_API_KEY`. El cliente `OpenAI.Chat.ChatClient` se inicializa con esa clave. Si la variable no está presente, el programa muestra un aviso y finaliza.

## Seguridad

- `configurar_entorno.sh` está en `.gitignore` y no se sube al repo.
- Mantén permisos restringidos en tu script local: `chmod 600 configurar_entorno.sh`.
- No publiques tu clave real en commits, issues ni archivos compartidos.

## Solución de problemas

- Mensaje: "Define OPENAI_API_KEY antes de ejecutar." → Exporta la variable o usa el script de entorno.
- Errores al llamar a la API de OpenAI → Revisa conectividad, modelo configurado y límites de tokens.
- Uso de tokens no disponible → La librería actual no expone conteo de tokens; se muestra aviso informativo.

## Notas

- Objetivo de framework: `net9.0` (ver `DocMCPFlowCSharp.csproj`). Asegúrate de tener .NET 9 instalado.
- Modelos: puedes cambiar el modelo con `OPENAI_MODEL`.
- Límite de tokens: se aplica de forma best‑effort según la versión del SDK `OpenAI`. El cliente intenta usar `ChatCompletionOptions` y establecer `MaxOutputTokens`/`MaxTokens` si existen; si no, continúa sin límite explícito.
