<header>

![Banner](https://github.com/user-attachments/assets/5b933a56-0ece-452a-99c0-1a641485a6b9)

# **Benchmark**

_**Herramienta para medir y comparar el rendimiento, ideal para optimizar proyectos y analizar el coste de diferentes técnicas gráficas.**_


</header>
   
<footer>
   
## Después de crear el repositorio desde la plantilla, asegúrate de revisar lo siguiente:

### 📸 Social Preview
- [ ] Sube una imagen `preview.png` personalizada en `Settings → Social Preview`.

### ⚙️ Repository Features
Desactiva funciones que no necesitas en `Settings → Features`:

- [ ] Desactivar **Projects**
- [ ] Desactivar **Wiki**
- [ ] Desactivar **Packages**
- [ ] Desactivar **Environments** (Deployments)
- [ ] Confirmar que **Releases** sigue activado ✅

### 🎨 Personalización visual
- [ ] Cambiar imagen del banner de portada.
- [ ] Dejar Topics necesarios.


</footer>

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

