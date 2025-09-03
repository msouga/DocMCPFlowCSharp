# Informe sobre implementación de Microsoft Teams Rooms en AD con MFA

Este documento explica de forma clara y accionable las decisiones, controles y pasos necesarios para desplegar Microsoft Teams Rooms (MTR) en un Active Directory/Entra ID que exige MFA. Está pensado para audiencias mixtas: gerencia de TI (técnico) y gerencia general (negocio). El enfoque prioriza continuidad operativa, gobernanza y mitigación de riesgos.

A lo largo del informe se presentan dos modelos principales de despliegue —con Intune y sin Intune—, controles compensatorios, riesgos y un roadmap corto, medio y largo plazo. Cada sección incluye los elementos que el equipo técnico debe ejecutar y los puntos que la dirección debe validar para la toma de decisiones.

## Resumen ejecutivo

Microsoft Teams Rooms ofrece una experiencia de reuniones integradas, pero no soporta MFA interactivo en las cuentas de sala. Esto obliga a diseñar políticas de acceso condicional y controles de red para preservar la seguridad sin afectar la disponibilidad de las salas.

Este informe propone dos rutas: (A) integrar MTR con Intune y usar device compliance como factor de control, que es la opción más robusta; y (B) aplicar controles compensatorios basados en Named Locations, segmentación de red y rotación de credenciales, pensada como solución puente cuando no hay Intune. Para cada ruta se listan pasos, riesgos y métricas.

# 1. Contexto y objetivos

El objetivo de este documento es ofrecer una guía para seleccionar la mejor forma de desplegar Teams Rooms en un tenant que exige MFA. Presenta decisiones de seguridad, impacto en operación y recomendaciones de inversión.

El alcance incluye Teams Rooms en Windows y en Android, el uso de cuentas de recurso (resource accounts). No se incluyen instrucciones físicas de instalación de hardware.

# 2. Breve marco técnico (qué hace MTR respecto a autenticación)

Teams Rooms utiliza cuentas de recurso que inician sesión de forma no interactiva; por diseño no pueden aprobar un segundo factor mediante push u otros mecanismos interactivos. Por lo tanto, forzar MFA en estas cuentas impide el inicio y deja las salas fuera de servicio.

Microsoft recomienda compensar la falta de MFA interactivo usando factores no interactivos: device compliance (registro en Intune u otro MDM que reporte cumplimiento) y named locations (IP/Red confiable). El diseño de políticas debe contemplar estas opciones y prevenir exclusiones inseguras.

## 2.1 Modelo de autenticación de Teams Rooms

Las cuentas de sala son cuentas tipo "resource" gestionadas por administración; su flujo de autenticación suele ser no interactivo (ROPC o device code según plataforma). No está pensada la presencia de un humano que apruebe MFA durante el sign‑in.

Como consecuencia, la política de seguridad debe excluir explícitamente a estas cuentas de las reglas que exigen MFA interactivo y aplicarles una política específica que combine controles alternativos de seguridad. Esto evita interrupciones operativas.

## 2.2 Relevancia de Device Compliance e Intune

El uso de Intune (o un MDM que integre con Entra) permite exigir device compliance como requisito en Conditional Access. Esto introduce un factor de seguridad que no requiere interacción humana y es la forma recomendada para proteger dispositivos compartidos.

Sin Intune, la organización debe aplicar controles compensatorios basados en red y operaciones; estos reducen exposición pero no igualan la robustez de device compliance, por lo que deben considerarse soluciones temporales con hoja de ruta hacia MDM.

# 3. Diseño de despliegue: Modelo A — Con Intune (recomendado)

Este modelo recomienda enrolar las salas en Intune y usar políticas de cumplimiento para permitir el acceso a las cuentas de sala sin MFA interactivo. Es la alternativa que ofrece mayor trazabilidad, capacidades de respuesta y cumplimiento.

Requiere inversión inicial en licenciamiento y esfuerzo operativo para enrolamiento y pruebas, pero reduce la complejidad operativa a mediano y largo plazo. Es la opción más alineada con buenas prácticas de seguridad para dispositivos compartidos.

## 3.1 Principales características

Con Intune se puede exigir que el dispositivo esté marcado como "compliant" antes de permitir el acceso a los servicios de Teams/Office; la política CA dirigida a las cuentas de recurso valida compliance y evita el prompt MFA interactivo.

Desde negocio, esto permite mantener la política global de MFA para usuarios humanos mientras se gestiona la superficie de ataque de las salas mediante posture-management centralizado, inventario y reporting automático.

## 3.2 Precondiciones y requisitos

Licenciamiento: Intune (o soluciones equivalentes) y, según políticas, Entra ID P1/P2 para capacidades de Conditional Access avanzadas. Además, coordinar con fabricantes del kit de sala para garantizar compatibilidad con enrolamiento.

Requisitos operativos: perfiles de compliance definidos (antimalware activo, firewall, parcheo mínimo), identidades de servicio controladas y un proceso de enrolamiento y reprovisionamiento documentado para las salas.

# 4. Diseño de despliegue: Modelo B — Sin Intune (control compensatorio)

Si no hay Intune, la alternativa inmediata es usar controles de red y operativos: Named Locations, segmentación de red, contraseñas de recurso fuertes con rotación automática y monitoreo intensivo. Es una solución puente, no la final.

Esta opción minimiza inversión inicial pero incrementa la carga operacional y mantiene un mayor riesgo residual ante compromisos de red. Debe documentarse como control compensatorio con fecha objetivo para migrar a MDM.

## 4.1 Principales características

Permitir acceso a las cuentas de sala solo desde ubicaciones de red confiables (Named Locations) y aislar las salas en segmentos de red con egress control restringido. Esto reduce exposición remota sin exigir device compliance.

Operativamente requiere sistemas de firewall/NAT bien configurados, IPs públicas estáticas o NAT determinista para identificar las salas y mecanismos automáticos para rotación de credenciales y detección de anomalías.

## 4.2 Limitaciones y riesgos

El riesgo principal es que se traslade la protección al perímetro de red: si dicho perímetro se compromete (VPN, NAT, IPSpoofing, insider threat), el atacante puede acceder sin MFA. Por eso la segmentación y la monitorización son imprescindibles.

La operación de rotación y auditoría exige disciplina; sin automatización la carga operativa crece y se vuelve error‑prone. Además, este modelo puede ser menos amigable para auditorías de cumplimiento a largo plazo.

# 5. Seguridad: amenazas mitigadas y exposiciones al no tener MFA interactivo

Evitar MFA interactivo en cuentas MTR evita interrupciones de servicio y asegura continuidad de reuniones, que para muchas operaciones es crítico. Las políticas CA alternativas reducen la probabilidad de accesos remotos no autorizados.

Sin embargo, no tener MFA interactivo abre vectores que son trasladados a la red y a la gestión de credenciales: compromisos de red o credenciales mal gestionadas son los principales riesgos residuales que se deben mitigar.

## 5.1 Qué se evita y por qué

Se evita la caída de la sala por prompts MFA no solucionables y se mantiene la experiencia de usuario esperada. Además, la combinación de Device Compliance + Named Locations entrega un control comparable a MFA sin interacción humana.

Desde negocio, asegurar continuidad de reuniones evita pérdidas por interrupción operativa y disrupciones en procesos críticos (ventas, operaciones, abastecimiento). Esto debe pesarse frente al riesgo técnico residual.

## 5.2 Qué nuevas exposiciones quedan abiertas

Exposición por compromiso de red: si un atacante logra moverse lateralmente o controlar una IP dentro de la Named Location, podrá iniciar sesión en la sala. Por tanto la protección de la red y la segmentación son críticas.

Exposición por credenciales: las cuentas de recurso se vuelven puntos únicos de fallo si no se rotan, almacenan correctamente y no se auditan. Implementar rotación automatizada y guardado en un vault es obligatorio.

# 6. Ventajas y desventajas comparadas (resumen para la gerencia)

La opción con Intune ofrece mayor seguridad, automatización y mejores credenciales para auditoría; implica coste y tiempo de implementación. Es la recomendada como objetivo estratégico.

La opción sin Intune reduce inversión inicial y se puede implementar rápido, pero carga la operación y deja un riesgo residual que requiere controles permanentes y monitoreo constante.

## 6.1 Opción A — Con Intune (recomendada)

Ventajas: posture management, trazabilidad, menor riesgo de compromiso y mejor postura ante auditorías. Además facilita automatización y reporting centralizado.

Desventajas: licenciamiento y esfuerzo de onboard de dispositivos, coordinación con fabricantes y necesidad de perfiles y excepciones bien diseñadas para evitar false positives.

## 6.2 Opción B — Sin Intune (control compensatorio)

Ventajas: rápida implementación y menor coste inmediato. Útil como puente operativo cuando no hay presupuesto o tiempo para un proyecto MDM.

Desventajas: mayor exposición si la red es comprometida, mayor carga operativa en rotación y auditoría, y menor capacidad de demostrar cumplimiento regulatorio a largo plazo.