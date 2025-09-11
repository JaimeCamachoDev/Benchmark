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

## Pruebas recomendadas para Quest 3 (VR)

Este proyecto incluye utilidades para medir rendimiento. Para Oculus Quest 3 es recomendable diseñar pruebas que cubran:

- **Fill‑rate y resolución**: medir escenas con muchos píxeles (quads a pantalla completa) para detectar cuellos de botella en GPU.
- **Complejidad geométrica**: usar rejillas de mallas con distintos números de vértices para estresar la carga de CPU/GPU.
- **Iluminación y post‑proceso**: comparar distintos números de luces, sombras y efectos URP.
- **Sistemas de partículas/VFX**: evaluar costes de efectos visuales frecuentes en VR.
- **Memoria y streaming**: comprobar impacto de texturas o modelos de gran tamaño.

### Logger de métricas en dispositivo

El script `QuestPerfLogger` guarda en `persistentDataPath/QuestPerf.csv` los tiempos de CPU/GPU y el nivel de batería. Añádelo a la escena principal para tener un HUD básico y un registro de datos durante las pruebas.

### Análisis automático en Google Sheets

Los datos enviados a la hoja de cálculo ahora incluyen:

- Valores de tiempo redondeados para facilitar la lectura.
- Detección del cuello de botella (CPU o GPU).
- Una valoración rápida sobre si el rendimiento es adecuado para Quest 3.
- Un campo de resumen con la comparación contra el presupuesto de tiempo de frame.

Para que estos nuevos campos aparezcan en Google Sheets, actualiza el script de Apps Script añadiendo las columnas `Bottleneck`, `Quest3Rating` y `Summary` al arreglo `HEADERS`.

