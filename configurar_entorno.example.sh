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

echo "✅ Plantilla de variables de entorno cargada (sin credenciales)."
echo "ℹ️ Copia a configurar_entorno.sh y añade tu clave real."
