# Azure Storage para programadores C\#

## Capítulo 1: Fundamentos de Azure Storage

Este capítulo introduce los principios esenciales del almacenamiento en la nube con un enfoque particular en cómo Azure Storage se convierte en una herramienta estratégica para programadores C#. Se analizan los beneficios de migrar desde entornos locales hacia soluciones en la nube, abordando temas como elasticidad, disponibilidad global y seguridad administrada. Al mismo tiempo, se hace una revisión crítica de las limitaciones, como costos de salida o riesgos de dependencia con un proveedor. El objetivo es que el lector entienda no solo la terminología, sino la filosofía detrás de Azure Storage: diseñar pensando en escalabilidad, resiliencia y control de costos.

### 1.1 Introducción al almacenamiento en la nube

Esta sección explica el cambio de paradigma que supone la nube frente al almacenamiento tradicional. Se detallan conceptos como escalabilidad elástica, redundancia geográfica y consumo basado en demanda, resaltando cómo estas características han transformado la forma en que se diseñan las aplicaciones modernas. Además, se abordan posibles desafíos, como la necesidad de conectividad permanente y los riesgos regulatorios asociados al manejo de datos en diferentes jurisdicciones.

#### 1.1.1 Conceptos básicos

Aquí se definen términos fundamentales como blobs, contenedores, cuentas de almacenamiento y niveles de redundancia. Estos conceptos se presentan de forma práctica, con ejemplos que muestran cómo se aplican en escenarios reales, desde aplicaciones web hasta sistemas críticos de datos. se dan ejemplos tanto en azure como en la nube de AWS y la nube de google para demostrar que si bien las implementaciones son distintas el concepto existe en todas las nubes

#### 1.1.2 Beneficios y limitaciones

Se detallan las principales ventajas de Azure Storage: costos ajustados al consumo, seguridad integrada y disponibilidad global. También se identifican sus limitaciones, como los cargos por egreso de datos y la pérdida de control físico sobre la infraestructura. El lector aprenderá a evaluar cuándo el beneficio compensa la limitación.

### 1.2 Tipos de cuentas de almacenamiento

En esta sección se explica cómo la elección del tipo de cuenta impacta directamente en las capacidades del sistema. Se diferencian las cuentas General Purpose v2, recomendadas por su versatilidad, y Blob Storage, diseñadas para cargas masivas de objetos.

#### 1.2.1 General Purpose v2

Es la opción más flexible, soportando blobs, colas, tablas y archivos. Se ilustra cómo permite consolidar múltiples servicios bajo una misma cuenta, facilitando la gestión y reduciendo la complejidad.

#### 1.2.2 Blob Storage

Diseñada para escenarios donde solo se necesita almacenamiento de objetos, esta cuenta optimiza el manejo de grandes volúmenes de datos no estructurados como imágenes, copias de seguridad o videos.

### 1.3 Seguridad

La seguridad es un pilar transversal en Azure Storage. Aquí se describen las medidas automáticas y configurables para proteger datos en reposo y en tránsito, junto con las mejores prácticas para programadores.

#### 1.3.1 Cifrado

Se explica el cifrado por defecto administrado por Microsoft y las opciones de BYOK (Bring Your Own Key) o HSM gestionados para mayor control. También se aborda el cifrado en tránsito mediante TLS.

#### 1.3.2 Autenticación

Esta sección describe las diferentes alternativas: claves de cuenta, SAS tokens, autenticación con Entra ID y Managed Identities. Se enfatiza la importancia de evitar prácticas inseguras como incrustar secretos en el código.

#### 1.3.3 Control de acceso basado en roles

Se presenta el modelo RBAC de Azure y cómo aplicar el principio de menor privilegio. Se incluyen ejemplos de asignación de roles en proyectos de desarrollo y producción para ilustrar su aplicación práctica.
