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
     export NODE_DETAIL_WORDS=0               # opcional (0: ilimitado, nodos hoja)
     export NODE_SUMMARY_WORDS=180            # opcional (nodos con hijos)
     export DEBUG=true                        # opcional (true: Info+Debug; false: solo Warning+Error)
     export USE_RESPONSES_API=false           # opcional (true usa /v1/responses con caché de input)
     export ENABLE_WEB_SEARCH=false           # opcional (true añade herramienta de búsqueda; requiere USE_RESPONSES_API=true)
     export CACHE_SYSTEM_INPUT=true           # opcional (cachea el system prompt)
     export CACHE_BOOK_CONTEXT=true           # opcional (cachea contexto del libro por corrida)
     export RESPONSES_STRICT_JSON=false       # opcional (usa text.format para forzar JSON)
     export STRIP_LINKS=false                 # opcional (true elimina hipervínculos del manual para impresión)
     # Opcionales para ejecución no interactiva (evita prompts):
     export TARGET_AUDIENCE="Programadores C# intermedios"
     export TOPIC="Azure Storage práctico desde C#"
     # Si NO usas INDEX_MD_PATH, puedes precargar también el título:
     # export DOC_TITLE="Azure Storage para programadores C#"
     ```

3) Compilar y ejecutar:
   ```bash
   dotnet build
   dotnet run -- -p executionparameters.config
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
- `NODE_DETAIL_WORDS`: palabras objetivo para nodos hoja (sin hijos). 0 = ilimitado.
- `NODE_SUMMARY_WORDS`: palabras objetivo para nodos con hijos (overview/resumen). No se aplica si el nodo es hoja.
  # (Se eliminó el límite de llamadas por contenido; usa DEMO_MODE para pruebas 2×2.)
- `DEBUG`: si `true`, el logging incluye niveles Information y Debug; si `false`, solo Warning y Error. Defecto: `true`.
- `USE_RESPONSES_API`: si `true`, usa el endpoint `/v1/responses` con soporte de caché de input; si `false`, usa el cliente Chat.
 - `ENABLE_WEB_SEARCH`: si `true` y `USE_RESPONSES_API=true`, se añade la herramienta `web_search` para permitir búsquedas web automáticas. Para contenido (nodos hoja), se pide citar 3–5 fuentes con URL al final.
- `CACHE_SYSTEM_INPUT`: si `true`, marca el system prompt como cacheable en Responses.
- `CACHE_BOOK_CONTEXT`: si `true`, cachea un bloque estable por corrida (título, público, tema y TOC).
- `INDEX_MD_PATH`: ruta a un archivo Markdown con el índice (opcional). Si se define, el programa carga el título (H1) y la estructura (H2=capítulos, H3=subcapítulos, H4=sub-sub, etc.) desde el archivo y omite la generación de índice por LLM o demo.
- `CUSTOM_MD_BEAUTIFY`: si `true` aplica reglas propias de embellecido (espaciado de listas, etc.) además de la normalización obligatoria con Markdig. Si `false`, solo se ejecuta Markdig. Por defecto `true`.
- `STRIP_LINKS`: si `true`, elimina hipervínculos del manual (convierte `[texto](url)` en `texto` y borra URLs sueltas/autolinks). Útil para impresión.

## Archivo de parámetros de ejecución (opcional)

- Puedes pasar un archivo JSON con parámetros y respuestas para ejecución no interactiva.
- Nombre por defecto: `executionparameters.config` (o `executionparameters.json`) en el cwd. También puedes especificarlo con `--params <ruta>` o `-p <ruta>`.
- Ejemplo mínimo (`executionparameters.config`):
  ```json
  {
    "openai_api_key": "sk-...",
    "openai_model": "gpt-5-mini",
    "use_responses_api": true,
    "enable_web_search": true,
    "index_md_path": "./indexprueba.md",
    "target_audience": "Principiante",
    "topic": "Backup de Azure para C#",
    "dry_run": false,
    "strip_links": false
  }
  ```
- Claves soportadas (se mapean a variables de entorno equivalentes):
  - openai_api_key, openai_model, max_tokens, http_timeout_seconds
  - dry_run, show_usage, treat_refusal_as_error, demo_mode, debug
  - node_detail_words, node_summary_words
  - use_responses_api, enable_web_search, cache_system_input, cache_book_context, responses_strict_json, openai_beta_header
  - index_md_path, custom_md_beautify, strip_links
  - target_audience, topic, doc_title
 - `TARGET_AUDIENCE` y `TOPIC`: si se definen, el programa no te los pedirá por consola. Útiles para ejecución desatendida.
 - `DOC_TITLE`: si no usas `INDEX_MD_PATH`, puedes precargar el título con esta variable.

Estas opciones se leen en `Implementations/EnvironmentConfiguration.cs` y en `Program.cs`.

## Ejecución

Al ejecutar, el programa:

1) Verifica `OPENAI_API_KEY`. Si falta, muestra un aviso y termina.
2) Crea un directorio de corrida bajo `back/AAAA-MM-DD-HH-mm-ss/` y guarda el log allí.
3) Inicializa el cliente OpenAI (`Implementations/OpenAiSdkLlmClient.cs`).
4) Orquesta la generación con `Orchestration/BookGenerator.cs` y escribe el manuscrito (`Implementations/MarkdownManuscriptWriter.cs`). También deja copias de `manuscrito.md` y `manuscrito_capitulos.md` en ese directorio de `back/`.

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
 - Responses API: para gpt-5 / gpt-5-mini / gpt-5-nano puedes activar `USE_RESPONSES_API=true` y aprovechar caché de input efímero (puede requerir un header beta, ver `OPENAI_BETA_HEADER`). Asegúrate de que el system/contexto estable no cambie entre llamadas para maximizar hits.
  - Índice por archivo: si defines `INDEX_MD_PATH`, usa un `.md` con:
   - `# Título del documento`
   - `## Capítulo`
   - `### Subcapítulo`
   - `#### Sub-sub` (opcional)
   - Opcional: un párrafo de resumen inmediatamente debajo de cualquier encabezado será tomado como el “sumario” de esa sección y NO se generará uno nuevo (se respeta tal cual).
   El título se precarga del H1 y el índice se toma de los encabezados. Puedes poner numeraciones en los H2/H3/H4, pero el parser las limpia y conserva solo el texto. También elimina el prefijo “Capítulo N:” en H2. Luego el programa genera resúmenes y contenido igual que en el flujo normal.

## Búsqueda Web y citas (opcional)

- Para habilitar búsquedas: `export USE_RESPONSES_API=true` y `export ENABLE_WEB_SEARCH=true`.
- El cliente añade la herramienta oficial `web_search` y permite al modelo consultar la web cuando falte contexto.
- En secciones de contenido (no en overviews), se indica citar 3–5 fuentes al final bajo "Fuentes".
- Si usas el cliente Chat (sin Responses), `ENABLE_WEB_SEARCH` no tiene efecto (se avisa por consola).

## Sugerencias de gráficos

- Al finalizar el contenido, el orquestador genera `graficos_sugeridos.md` en la carpeta `back/<timestamp>/` con propuestas de diagramas por sección.
- Formatos preferidos: PlantUML; si no procede, Mermaid; si ninguno aplica, descripción textual.
- Cada entrada indica sección destino, objetivo, ubicación recomendada y bloque de código (cuando corresponde).
- Además, se insertan marcadores visibles al final de cada sección en ambos manuscritos (`manuscrito.md` y `manuscrito_capitulos.md`) con el formato:
  - `[DIAGRAMA] <num> <título> → <nombre> | Formato: <plantuml|mermaid|texto> | Ubicación: <start|end|before_para:N|after_para:N>`
  - Sirven como guía de dónde insertar el gráfico; si hay código, consulta `graficos_sugeridos.md`.
