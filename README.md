<header>

# **Benchmark**

_**Herramienta para medir y comparar el rendimiento, ideal para optimizar proyectos y analizar el coste de diferentes técnicas gráficas.**_

</header>
   

## Sistema de benchmark por departamentos (Programación / Animación / Modelado / VFX)

El presupuesto de rendimiento del proyecto se reparte a partes iguales entre las 4 áreas
que pueden hacer caer los fps en Quest 3 / Pico 4:

| Departamento | 25% | Qué se mide |
|---|---|---|
| **Programación** | ✅ | Coste de CPU puro: raycasts, GC allocations, IA, lógica de juego |
| **Animación** | ✅ | Animator/Skinned Mesh vs VAT (Vertex Animation Textures), nº de personajes simultáneos |
| **Modelado** | ✅ | Nº de triángulos, draw calls, batching/instancing, LOD |
| **VFX** | ✅ | Shaders, materiales, sombras, partículas, post‑proceso |

El motor de medición es el mismo para las 4 áreas (`Assets/ShaderBench/BenchmarkRunner.cs` +
`BenchmarkCase.cs`), así que aprender a usarlo una vez sirve para todos los departamentos.

### Presupuesto de frame en Quest 3 / Pico 4

Ambos dispositivos usan el mismo chip (Snapdragon XR2 Gen 2) y modos de refresco similares:

| Refresco | Presupuesto de frame |
|---|---|
| 72 Hz | 13.9 ms |
| 90 Hz | 11.1 ms |
| 120 Hz | 8.3 ms |

Reparte ese presupuesto entre CPU y GPU (orientativo: ~8‑10 ms cada uno) dejando 2‑3 ms de
margen para el throttling térmico. `BenchmarkRunner` compara el p95 de CPU/GPU contra
`targetFPS` y marca cada caso como `Good`/`Medium`/`Bad` automáticamente.

### Cómo crear un caso de benchmark (cualquier departamento)

1. En Unity: `Assets > Create > ShaderBench > Benchmark Case` (o usa los generadores de
   ejemplos del menú `JaimeCamachoDev/ShaderBench/...` para partir de una plantilla).
2. Arrastra tu prefab (tu script, tu personaje animado, tu malla, tu efecto) al campo `Prefab`.
3. Marca el campo **Departamento** (`Programming`, `Animation`, `Modeling` o `VFX`).
4. Ajusta `gridX`/`gridY`/`spacing` para decidir cuántas copias se instancian a la vez.
5. Añade el caso a la lista `cases` de un `BenchmarkRunner` en la escena y pulsa **Play** → **[ESPACIO]** para lanzar la medición ([ESC] cancela).

Casos ya incluidos como ejemplo:

- **VFX/Rendering**: `Baseline_Unlit_InstancingON`, `URP_Lit_*`, `URP_MAS_*`, `URP_SimpleLit_*`.
- **Animación**: `VAT`, `SkinnedMesh`, `moveAnimation`, `moveSpline` (comparan Animator/Skinned Mesh vs VAT).
- **Programación**: genera `Programming_RaycastStress` desde el menú `JaimeCamachoDev/ShaderBench/Create Programming Example Case` (usa `ProgrammingStressUnit.cs`, en `Assets/1-Programming/`, como plantilla para vuestras propias cargas de CPU).
- **Modelado**: genera `Modeling_LowPoly_Baseline` / `Modeling_HighPoly_Ankh` desde `JaimeCamachoDev/ShaderBench/Create Modeling Example Cases`.

### Análisis automático en Google Sheets

Cada fila subida a la hoja de cálculo incluye ahora el **Departamento** además de:

- Valores de tiempo redondeados para facilitar la lectura.
- Detección del cuello de botella (CPU o GPU).
- Una valoración rápida sobre si el rendimiento es adecuado para Quest 3 / Pico 4.
- Un campo de resumen con la comparación contra el presupuesto de tiempo de frame.

Esto permite filtrar/pivotar la hoja por departamento para ver de un vistazo si algún área
concreta está gastando más de su 25% del presupuesto.

### Test de eficiencia de material/shader (packing, VFX, etc.)

Para responder "¿qué material/packing es el más eficiente para usar como estándar en VR?"
un único número a una densidad fija no basta (ver por qué en el apartado de metodología
más abajo). `BenchmarkCase`/`BenchmarkRunner` tienen 3 añadidos pensados exactamente para
este caso, sin afectar a los casos ya existentes (todo es opt-in):

- **`densitySweep`** (lista de enteros, p. ej. `[10, 50, 100, 300]`): en vez de un único
  `gridX`×`gridY` fijo, el caso se repite una vez por cada nº de instancias de la lista. Deja
  vacío este campo para el comportamiento de siempre (un único grid fijo).
- **`fillViewport`**: en vez de esparcir el grid libremente, coloca las instancias como un
  mosaico que cubre TODO el campo de visión de la cámara a `fillDistance` metros. Así el nº
  de píxeles sombreados es siempre el mismo (toda la pantalla) sea cual sea el nº de
  instancias, forzando que la GPU/el shader sean el cuello de botella real y aislando el
  coste por píxel del material. Pensado para prefabs simples (Quad/Plane) con el material a
  comparar, no para mallas complejas.
- **`packingScheme` / `textureSamplesPerInstance` / `variantNotes`**: metadatos libres para
  documentar qué estás comparando exactamente (p. ej. `"URP Lit estándar (M/AO/S)"` vs
  `"NOS 1-sample custom"`), que viajan también al CSV/Sheets.

Para usarlo: crea un `BenchmarkCase` por cada variante de material/packing que quieras
comparar (mismo prefab base, mismo `fillViewport` + `fillDistance`, solo cambia el material),
rellena `densitySweep` con al menos 2-3 valores, y en el `BenchmarkRunner` activa
**`Compute Material Efficiency Summary`**. Al terminar la ejecución, además del resumen
habitual, se pide a Sheets que calcule el **coste por instancia** (ms/instancia, por
regresión lineal sobre el barrido de densidad) — esa pendiente, no el número absoluto a una
densidad arbitraria, es la que realmente predice cómo escalará cada material en una escena
real con cientos de objetos.

En Sheets esto crea una pestaña `<hoja>_MaterialEfficiency` con CPU/GPU ms-por-instancia y
coste fijo por caso, ordenada de más a menos eficiente, con gráficos — y añade además una
fila por caso a una hoja persistente **`MaterialEfficiency_History`** que nunca se borra,
para poder comparar entre sesiones de test a lo largo del tiempo.

El Apps Script que procesa esto está versionado en `Tools/AppsScript/ShaderBenchWebApp.gs`.
Si lo cambias, cópialo también al editor de Apps Script de la hoja (Extensions > Apps
Script) y vuelve a desplegarlo (Deploy > Manage deployments > Edit > **New version**) para
que el cambio llegue a la URL que ya usa Unity. Esta versión también añade `Department` a
las cabeceras que Sheets reconoce — sin este cambio, la columna Department que ya subía
Unity se estaba descartando silenciosamente en el lado de Sheets.

### EfficientLit: material estándar candidato (2 texturas, mismo aspecto que Lit)

`Assets/2-Art/2-VFX/EfficientLit/EfficientLit.shader` es una implementación concreta de la
recomendación de packing: mismo aspecto PBR metálico que URP Lit, pero con **2 texture
samples fijos** (garantizados por código) en vez de los 3-4 de Lit.

- **`_BaseMap`**: RGB = Albedo, A = Smoothness.
- **`_DataMap`**: R = Height (escala de grises), G = libre/sin usar, B = AO, A = Metallic.
  **Importante**: `_DataMap` debe importarse como textura **Default/Lineal**, nunca como
  "Normal Map".

La normal **no** se lee de un normal map pre-horneado en X/Y — se deriva en el propio
shader a partir del canal de Height vía `ddx`/`ddy` (la misma técnica que el nodo
**"Normal From Height"** de Shader Graph), sin añadir texture samples extra (es coste de
ALU, no de ancho de banda). Es deliberado: si vuestro origen de datos (p. ej. un paquete
MOHS) solo tiene un canal de Height real y no un normal map con X/Y ya calculados, intentar
leer ese Height como si fueran componentes X/Y de una normal produce relieve prácticamente
aleatorio — así se detectó este bug la primera vez (ver más abajo).

El menú `JaimeCamachoDev/ShaderBench/Create EfficientLit vs Lit Comparison` crea de golpe
dos `BenchmarkCase` listos para comparar (un Quad con `EfficientLit`, otro con `Lit` de
referencia, ambos en modo `fillViewport` + `densitySweep`): añádelos a un `BenchmarkRunner`,
activa `Compute Material Efficiency Summary` y dale a Play para ver la diferencia real de
GPU ms/instancia en `_MaterialEfficiency`.

**Aviso honesto**: este shader está escrito a mano (HLSL/ShaderLab) usando las APIs de
iluminación de URP más estables (`SurfaceData`/`InputData`/`UniversalFragmentPBR`,
reutilizando los passes ShadowCaster/DepthOnly/DepthNormals/Meta del propio Lit vía
`UsePass`), pero no ha podido compilarse ni probarse en un Editor real antes de este commit.
Ábrelo en Unity y confirma que compila sin errores antes de usarlo en producción; si algo
falla, lo más probable es un desajuste puntual de campos de `SurfaceData`/`InputData` con
vuestra versión exacta de URP (17.0.4), fácil de corregir mirando el error de consola.

#### Historial: por qué la primera versión daba relieve "aleatorio" con tangentes

La primera versión de este shader asumía que `_DataMap.rg` ya eran componentes X/Y de una
normal pre-horneada, y reconstruía Z (`z = sqrt(saturate(1 - dot(xy,xy)))`). Con contenido
real tipo MOHS (Metallic/Occlusion/**Height**/Smoothness) eso está mal: Height es un único
canal en escala de grises, no un par X/Y — leerlo como si fuera X/Y produce un relieve
prácticamente aleatorio. Esto se detectó comparando con un Shader Graph equivalente hecho a
mano que sí funcionaba: usaba el nodo **"Normal From Height"** sobre el canal R (Height),
no una reconstrucción de X/Y. La versión actual del shader ya usa esa misma técnica.

Pista relacionada que llevó hasta aquí: desactivar las tangentes del FBX "ocultaba" el
error — pero no lo arreglaba. Sin tangentes, `bitangentWS` colapsa a cero y el shader cae a
mostrar solo la normal geométrica lisa (sin relieve), así que cualquier relieve mal
calculado deja de verse simplemente porque deja de calcularse relieve alguno. Con la versión
actual, las tangentes del FBX deben estar **activadas** para que el relieve se vea.

Si el relieve sale al revés (cóncavo donde debería ser convexo), prueba el toggle
**`Invert Height`** del material antes de re-exportar texturas — es la ambigüedad habitual
de "blanco = alto" vs "blanco = bajo" según la herramienta de origen.

#### Alternativa sin riesgo: construirlo en Shader Graph a mano (~10 min)

Si preferís tenerlo en Shader Graph (más accesible para el equipo artístico, y sin ningún
riesgo de compatibilidad de versión porque Shader Graph genera el HLSL correcto para vuestra
versión de URP automáticamente):

1. `Create > Shader Graph > URP > Lit Shader Graph`. En el Graph Inspector: **Workflow =
   Metallic**, **Surface = Opaque**.
2. Añade 2 propiedades `Texture2D`: `_BaseMap` (default blanco) y `_DataMap` (default gris
   neutro, y en el asset de la textura real: sin sRGB, importada como Default).
3. Nodo `Sample Texture 2D` para `_BaseMap` → `Split`: RGB a **Albedo**, A a **Smoothness**.
4. Nodo `Sample Texture 2D` para `_DataMap` (Type = Default, no "Normal") → `Split` en 4:
   R al nodo **`Normal From Height`** (entrada "In"; "Strength" como propiedad ajustable),
   B a **Occlusion**, A a **Metallic**. El canal G queda libre/sin usar.
5. `Normal From Height` → conecta su salida directo al puerto **Normal (Tangent Space)**
   del Master Stack.
6. Guarda, crea un Material a partir del graph, asigna `_BaseMap`/`_DataMap` y compáralo con
   el mismo `BenchmarkCase` de arriba (cambia el material del caso "EfficientLit" por este).

#### Por qué un único número no basta para esta decisión

Si comparáis materiales/shaders con un solo grid a una sola densidad, es fácil que el
resultado sea "todos parecidos" sin que eso signifique que lo sean de verdad: si el test
está limitado por CPU (compara `CPU_ms_avg` y `GPU_ms_avg`: si CPU domina, la GPU nunca tuvo
ocasión de mostrar diferencias de coste de shader) o si la cobertura de píxeles es baja, el
coste real del shader/packing queda enmascarado por el propio overhead del test. `fillViewport`
+ `densitySweep` + mirar CPU/GPU por separado (no solo el "Bound") existen para atajar
justo ese problema.

### Herramientas complementarias en dispositivo real

`BenchmarkRunner` mide dentro del Editor/Player vía `FrameTimingManager`, pero para validar
en el propio Quest 3 / Pico 4 conviene combinarlo con:

- **OVR Metrics Tool** + **ovrgpuprofiler** (Meta): overlay y CSV en el propio headset, con throttling térmico y stale frames.
- **Snapdragon Profiler / RenderDoc**: captura de frame a bajo nivel cuando el cuello de botella no está claro.
- Bloquear el nivel de CPU/GPU antes de medir, para que las comparaciones entre ejecuciones sean válidas.

## PerfLab: analizar UN solo elemento (arrastrar → Play → leer el resultado)

`ShaderBench` está pensado para comparar muchos casos a fondo (rejillas, CSV, Sheets). Para
el día a día de cualquier departamento (técnico o artístico) que solo necesita saber
**"¿cuántos ms cuesta ESTO y entra en presupuesto?"**, hay un segundo sistema en paralelo,
totalmente independiente, en `Assets/PerfLab/`. No toca ni depende de `ShaderBench`.

### Cómo se usa

1. Menú `JaimeCamachoDev/PerfLab/Create Analyzer In Scene` (crea un GameObject `PerfLab Analyzer` en la escena abierta).
2. Arrastra tu elemento (script, personaje, malla, efecto VFX... cualquier cosa) al campo **Subject Prefab**: puede ser un prefab del Proyecto (se instancia solo) o un objeto que ya tengas colocado en la escena (se usa tal cual, en su sitio, sin duplicarlo).
3. Elige **Department** y el **Target Hz** del dispositivo: `30 / 60 / 72 / 90 / 120`.
4. Dale a **Play**. Sin teclas ni pasos extra.

### Qué hace por dentro

1. Mide la escena con el elemento **apagado** un par de segundos → línea base (todo con `Update()`, sin corrutinas ni esperas ligadas al renderizado — funciona igual si tienes en foco la pestaña Game o la Scene).
2. Activa el elemento y mide **en vivo y de forma continua** (ventana móvil, no un único disparo) la diferencia de CPU/GPU frente a esa línea base.
3. El resultado se muestra en dos sitios a la vez:
   - Un **panel flotante en el mundo** que siempre mira a la cámara (se ve dentro del propio casco Quest 3/Pico 4, colocado justo encima del elemento).
   - Un **overlay simple en pantalla** (para editor/desktop).
4. El cap (límite en ms) es siempre automático: `(1000 / Hz) / 4`, es decir, el 25% del presupuesto de frame que le toca a ese departamento a los Hz elegidos. No hay que configurar nada más.
5. Si entra en el cap: panel verde, "✔ DENTRO DEL PRESUPUESTO". Si no entra: panel y overlay se ponen **en rojo grande**, "✖ NO VÁLIDO — FUERA DE PRESUPUESTO", imposible de pasar por alto.
6. Sin CSV ni Sheets — es una herramienta de comprobación rápida, no de archivo histórico (para eso está `ShaderBench`).

### Por qué a veces el número "baila" (sube al azar y vuelve a 0)

El coste de un solo elemento suelto puede ser más pequeño que el propio "ruido" normal de
frame a frame (GC, drivers, otras ventanas del Editor...). Si ese ruido es mayor que el
elemento, restar línea base menos medición da un número que salta. Para minimizarlo:

- **VSync**: con VSync activado el frame se redondea a múltiplos del refresco (16.6 ms a
  60 Hz, 13.9 ms a 72 Hz...), así que un coste pequeño no se nota hasta que empuja el frame
  a pasarse de ese múltiplo entero — de ahí las lecturas "en escalón" (0 y luego un salto
  grande). Por eso `PerfLabAnalyzer` fuerza **VSync OFF y fps sin límite** mientras mide
  (`Force Uncapped Framerate`, activado por defecto) y lo restaura al salir de Play.
- **Media recortada**: en vez de una media normal, se descarta el 15% de muestras más altas
  y más bajas antes de promediar, tanto en la línea base como en la ventana en vivo. Así un
  pico puntual de GC no contamina el resultado, pero si el elemento realmente sube el coste
  de forma sostenida, sí se refleja.
- **Ventanas más largas**: por defecto la línea base mide 3 s y la ventana en vivo 2.5 s
  (antes eran más cortas). Cuanto más grande el elemento, antes se estabiliza el número;
  para efectos muy baratos puede convenir alargar aún más `Baseline Seconds`.
- **El Editor mete ruido que un build no tiene**: overhead del propio Editor, la ventana
  Scene renderizando en paralelo si está visible, Live reload, etc. Para el número
  definitivo (el que decide si algo entra o no en el presupuesto real), valida siempre en
  una **build de desarrollo en el propio Quest 3/Pico 4**, no solo en el Editor.
- Si un elemento es tan barato que ni con esto se estabiliza, probablemente su coste real
  esté por debajo del ruido de medición — es decir, es prácticamente gratis comparado con el
  presupuesto del departamento, lo cual también es una respuesta válida.
