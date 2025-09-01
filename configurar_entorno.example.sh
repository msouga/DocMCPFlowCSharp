#!/bin/bash

# --- Configuración de Entorno (Plantilla) ---
#
# Uso recomendado:
#   cp configurar_entorno.example.sh configurar_entorno.sh
#   # Edita configurar_entorno.sh y coloca tu clave real
#   source configurar_entorno.sh
#
# Nota: No incluyas claves reales en este archivo de ejemplo.

# --- Variable Requerida ---
# Reemplaza el valor por tu clave real de OpenAI.
export OPENAI_API_KEY="REPLACE_WITH_YOUR_OPENAI_API_KEY"

# --- Variables Opcionales ---
# Puedes ajustar estas variables según necesidad; se asignan valores por defecto
# solo si no están definidas en tu entorno.

# Modelo (p. ej., "gpt-4.1-mini", "gpt-5-mini").
export OPENAI_MODEL="${OPENAI_MODEL:-gpt-5-mini}"

# Modo de prueba (true/false). Si es 'true', no genera capítulos completos.
export DRY_RUN="${DRY_RUN:-true}"

# Mostrar uso de tokens al finalizar (true/false).
export SHOW_USAGE="${SHOW_USAGE:-true}"

# Límite de tokens por llamada a la API.
export OPENAI_MAX_COMPLETION_TOKENS="${OPENAI_MAX_COMPLETION_TOKENS:-4096}"

# Tiempo de espera en segundos para la respuesta de la API.
export OPENAI_HTTP_TIMEOUT_SECONDS="${OPENAI_HTTP_TIMEOUT_SECONDS:-300}"

# Tratar una negativa del modelo como error fatal (true/false).
export TREAT_REFUSAL_AS_ERROR="${TREAT_REFUSAL_AS_ERROR:-true}"

# Modo demo para limitar el índice (2 capítulos × 2 subcapítulos).
export DEMO_MODE="${DEMO_MODE:-true}"

# Longitud objetivo por subcapítulo (0 = ilimitado)
# Longitud objetivo por nodo hoja (0 = ilimitado)
export NODE_DETAIL_WORDS="${NODE_DETAIL_WORDS:-0}"

# Longitud objetivo del overview (solo nodos con hijos)
export NODE_SUMMARY_WORDS="${NODE_SUMMARY_WORDS:-180}"

# Nivel de logging (si DEBUG=true incluye Info y Debug; si false solo Warning y Error)
export DEBUG="${DEBUG:-true}"

# Alternar API de Responses (cache de input) vs Chat
export USE_RESPONSES_API="${USE_RESPONSES_API:-false}"
export CACHE_SYSTEM_INPUT="${CACHE_SYSTEM_INPUT:-true}"
export CACHE_BOOK_CONTEXT="${CACHE_BOOK_CONTEXT:-true}"
export RESPONSES_STRICT_JSON="${RESPONSES_STRICT_JSON:-false}"
export ENABLE_WEB_SEARCH="${ENABLE_WEB_SEARCH:-false}"   # si true y USE_RESPONSES_API=true, habilita herramienta de búsqueda web

# Si deseas cargar el índice desde un archivo Markdown en vez de generarlo:
#   Formato: H1 título, H2 capítulos, H3 subcapítulos, H4 sub-sub.
# export INDEX_MD_PATH="/ruta/a/indice.md"

# Habilitar/deshabilitar el embellecedor personalizado (Markdig siempre corre)
export CUSTOM_MD_BEAUTIFY="${CUSTOM_MD_BEAUTIFY:-true}"

# — Opcionales para ejecución no interactiva —
# Si defines estas variables, el programa NO te preguntará por ellas:
#   - TARGET_AUDIENCE: público objetivo (p.ej., "Programadores C# intermedios")
#   - TOPIC: descripción breve/tema del documento
#   - DOC_TITLE: título del documento (solo si NO usas INDEX_MD_PATH)
# export TARGET_AUDIENCE="Programadores C# intermedios"
# export TOPIC="Buenas prácticas y patrones con Azure Storage"
# export DOC_TITLE="Azure Storage para programadores C#"

echo "✅ Plantilla de variables de entorno cargada (sin credenciales)."
echo "ℹ️ Copia a configurar_entorno.sh y añade tu clave real."
