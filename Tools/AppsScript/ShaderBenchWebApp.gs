/******************************************************
 * ShaderBench ↔ Google Sheets Web App (v3)
 * - action = "append": añade filas a sheetName (crea pestaña si falta)
 * - action = "finalize": crea <sheetName>_Summary con análisis y gráficos
 * - action = "finalizeEfficiency": crea <sheetName>_MaterialEfficiency con
 *   el coste POR INSTANCIA (ms/instancia, por regresión lineal sobre el
 *   barrido de densidad que envía BenchmarkCase.densitySweep) y lo añade
 *   además a un histórico acumulado MaterialEfficiency_History para poder
 *   comparar entre sesiones de test a lo largo del tiempo.
 * - action = "clear": limpia sheetName
 *
 * Este fichero es una copia versionada del Apps Script real (Extensions >
 * Apps Script en la hoja de cálculo). Cuando lo cambies aquí, cópialo y
 * pégalo también en el editor de Apps Script, y vuelve a desplegar la Web
 * App (Deploy > Manage deployments > Edit > New version) para que el
 * cambio surta efecto en la URL ya usada por Unity.
 ******************************************************/

// ======= CONFIG =======
const DEFAULT_SHEET_NAME = 'Results';
const TOKEN = 'MiSecreto_987';  // <-- tu secreto

// Cabeceras que envía Unity (deben coincidir).
// Department / PackingScheme / TextureSamples / VariantNotes son nuevas: las
// filas antiguas que no las traigan simplemente quedan en blanco (r[h] ?? '').
const HEADERS = [
  'Timestamp','CaseName','Department','Repetition',
  'GridX','GridY','Count','Spacing',
  'GPUInstancing','Shadows','ReceiveShadows',
  'EnabledKW','DisabledKW',
  'CPU_ms_avg','CPU_ms_p95','CPU_ms_min','CPU_ms_max',
  'GPU_ms_avg','GPU_ms_p95','GPU_ms_min','GPU_ms_max',
  'FPS_avg','FPS_p95','FPS_min','FPS_max',
  'UnityVersion','RenderPipeline','GraphicsAPI','DeviceModel','OS',
  'Bottleneck','Quest3Rating','Summary',
  'PackingScheme','TextureSamples','VariantNotes'
];

// --- Debug GET ---
function doGet(e) {
  return ContentService.createTextOutput('OK').setMimeType(ContentService.MimeType.TEXT);
}

// --- POST ---
function doPost(e) {
  const lock = LockService.getScriptLock(); lock.tryLock(30000);
  try {
    const body = e && e.postData && e.postData.contents ? e.postData.contents : '{}';
    const data = JSON.parse(body);

    if (TOKEN && (!data.token || data.token !== TOKEN)) return txt_('UNAUTHORIZED');

    const action = (data.action || 'append').toLowerCase();
    const sheetName = (data.sheetName || DEFAULT_SHEET_NAME).toString().substring(0, 80);

    if (action === 'append') {
      const rows = Array.isArray(data.rows) ? data.rows : [data];
      const appended = appendRows_(sheetName, rows);
      return json_({ ok: true, appended, sheet: sheetName });
    }

    if (action === 'finalize') {
      const targetFps = Number(data.targetFps || 60);
      const outSheet = finalizeRun_(sheetName, targetFps);
      return json_({ ok: true, finalized: true, sheet: sheetName, summarySheet: outSheet.getName() });
    }

    if (action === 'finalizeefficiency') {
      const targetFps = Number(data.targetFps || 60);
      const outSheet = finalizeMaterialEfficiency_(sheetName, targetFps);
      return json_({ ok: true, finalizedEfficiency: true, sheet: sheetName, summarySheet: outSheet.getName() });
    }

    if (action === 'clear') {
      clearSheet_(sheetName);
      return json_({ ok: true, cleared: true, sheet: sheetName });
    }

    return json_({ ok: false, error: 'Unknown action' });
  } catch (err) {
    return json_({ ok: false, error: String(err) });
  } finally {
    lock.releaseLock();
  }
}

// ===== Helpers de respuesta =====
function json_(obj){ return ContentService.createTextOutput(JSON.stringify(obj)).setMimeType(ContentService.MimeType.JSON); }
function txt_(s){ return ContentService.createTextOutput(String(s)).setMimeType(ContentService.MimeType.TEXT); }

// ===== Helpers de hoja =====
function getOrCreateSheet_(name){
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  return ss.getSheetByName(name) || ss.insertSheet(name);
}
function clearSheet_(name){ getOrCreateSheet_(name).clear(); }

// Inserta filas respetando HEADERS (crea cabeceras si no existen)
function appendRows_(sheetName, rows){
  const sh = getOrCreateSheet_(sheetName);
  if (sh.getLastRow() === 0) sh.appendRow(HEADERS);
  const out = rows.map(r => HEADERS.map(h => (r[h] ?? '')));
  if (out.length) sh.getRange(sh.getLastRow() + 1, 1, out.length, HEADERS.length).setValues(out);
  return out.length;
}

// -------- helpers numéricos (deja los demás como están) --------
function toNum(v) {
  if (typeof v === 'number') return v;
  if (v == null) return NaN;
  let s = String(v).trim().replace(/ /g,'').replace(/\s+/g,'');
  if (s.indexOf(',') >= 0 && s.indexOf('.') < 0) s = s.replace(',', '.');
  if (/[.,].*[.,]/.test(s)) s = s.replace(/[.,](?=.*[.,])/g, '');
  const n = Number(s);
  return isNaN(n) ? NaN : n;
}
function mean_(arr) {
  const nums = arr.map(toNum).filter(x => !isNaN(x));
  return nums.length ? nums.reduce((a,b)=>a+b,0)/nums.length : NaN;
}
// devuelve número redondeado o BLANCO si no es válido
function safe4(x){ return (typeof x === 'number' && isFinite(x)) ? Math.round(x*10000)/10000 : ''; }
function safe3(x){ return (typeof x === 'number' && isFinite(x)) ? Math.round(x*1000)/1000   : ''; }
function safe2(x){ return (typeof x === 'number' && isFinite(x)) ? Math.round(x*100)/100     : ''; }

// Regresión lineal simple (mínimos cuadrados): y = slope*x + intercept.
// "slope" es el número clave para comparar eficiencia real entre materiales:
// cuántos ms extra cuesta CADA instancia adicional. "intercept" es el coste
// fijo (overhead) que no depende del número de instancias.
function linreg_(xs, ys) {
  const n = xs.length;
  if (n === 0) return { slope: NaN, intercept: NaN, r2: NaN };
  if (n === 1) return { slope: NaN, intercept: ys[0], r2: NaN };

  let sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
  for (let i = 0; i < n; i++) {
    sumX += xs[i]; sumY += ys[i];
    sumXY += xs[i] * ys[i]; sumXX += xs[i] * xs[i];
  }
  const denom = n * sumXX - sumX * sumX;
  if (denom === 0) return { slope: 0, intercept: sumY / n, r2: 0 };

  const slope = (n * sumXY - sumX * sumY) / denom;
  const intercept = (sumY - slope * sumX) / n;

  const meanY = sumY / n;
  let ssTot = 0, ssRes = 0;
  for (let i = 0; i < n; i++) {
    const pred = slope * xs[i] + intercept;
    ssRes += Math.pow(ys[i] - pred, 2);
    ssTot += Math.pow(ys[i] - meanY, 2);
  }
  const r2 = ssTot === 0 ? 1 : 1 - ssRes / ssTot;
  return { slope, intercept, r2 };
}

// ====== Detección de pestaña válida ======
function findValidDataSheet_(preferredName){
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const candidates = [];
  const want = new Set(['CaseName','CPU_ms_avg','CPU_ms_p95','GPU_ms_avg','GPU_ms_p95']);

  function scoreSheet(sh){
    const vals = sh.getDataRange().getValues();
    if (vals.length <= 1) return {ok:false, rows:0, sh};
    const head = vals[0].map(x => String(x).trim());
    // ¿tiene todas las columnas clave?
    for (const k of want) if (head.indexOf(k) < 0) return {ok:false, rows:vals.length, sh};
    return {ok:true, rows:vals.length, sh};
  }

  // 1) Preferida
  const pref = ss.getSheetByName(preferredName);
  if (pref) {
    const s = scoreSheet(pref);
    if (s.ok) return pref;
    candidates.push(s);
  }
  // 2) Otras que parezcan "Run_*" y no sean _Summary / _MaterialEfficiency
  for (const sh of ss.getSheets()){
    const name = sh.getName();
    if (/^Run_/i.test(name) && !/_Summary$/i.test(name) && !/_MaterialEfficiency$/i.test(name)){
      const s = scoreSheet(sh);
      if (s.ok) candidates.push(s);
    }
  }
  if (candidates.length === 0) return pref || getOrCreateSheet_(preferredName);
  // elige la de más filas
  candidates.sort((a,b)=>b.rows - a.rows);
  return candidates[0].sh;
}

// -------- FINALIZE con escritura segura y gráfico dinámico (sin cambios) --------
function finalizeRun_(dataSheetName, targetFps){
  const sh = findValidDataSheet_(dataSheetName);                  // usa tu findValidDataSheet_ actual
  const values = sh.getDataRange().getValues();
  if (values.length <= 1) throw new Error('No hay datos para analizar en '+sh.getName());

  const header = values[0].map(h => String(h).trim());
  const idx = name => header.indexOf(name);

  const iCase   = idx('CaseName');
  const iCPUavg = idx('CPU_ms_avg');
  const iCPU95  = idx('CPU_ms_p95');
  const iGPUavg = idx('GPU_ms_avg');
  const iGPU95  = idx('GPU_ms_p95');
  const iFPSavg = idx('FPS_avg');

  if (iCase < 0) throw new Error('Falta columna CaseName.');

  // Agrupar por caso
  const groups = new Map();
  for (let r = 1; r < values.length; r++){
    const row = values[r];
    const name = String(row[iCase] || '').trim();
    if (!name) continue;

    const cpuA = iCPUavg >=0 ? toNum(row[iCPUavg]) : NaN;
    const cpuP = iCPU95  >=0 ? toNum(row[iCPU95])  : NaN;
    const gpuA = iGPUavg >=0 ? toNum(row[iGPUavg]) : NaN;
    const gpuP = iGPU95  >=0 ? toNum(row[iGPU95])  : NaN;
    const fpsA = iFPSavg >=0 ? toNum(row[iFPSavg]) : NaN;

    if (![cpuA,cpuP,gpuA,gpuP,fpsA].some(x => !isNaN(x))) continue;

    if (!groups.has(name)) groups.set(name, []);
    groups.get(name).push({ cpuA,cpuP,gpuA,gpuP,fpsA });
  }

  const summaryHeaders = [
    'CaseName','Reps',
    'CPU_ms_avg(mean)','CPU_ms_p95(mean)',
    'GPU_ms_avg(mean)','GPU_ms_p95(mean)',
    'Bound_ms_avg(mean)','FPS_from_bound','Headroom_ms@'+targetFps,'Pass','Notes'
  ];

  const targetBudget = 1000 / targetFps;
  const rowsOut = [];
  let anyCPU = false, anyGPU = false;

  groups.forEach((arr, name) => {
    const cpuAvgMean = mean_(arr.map(r => r.cpuA));
    const cpuP95Mean = mean_(arr.map(r => r.cpuP));
    const gpuAvgMean = mean_(arr.map(r => r.gpuA));
    const gpuP95Mean = mean_(arr.map(r => r.gpuP));
    const fpsAvgMean = mean_(arr.map(r => r.fpsA));

    if (!isNaN(cpuAvgMean)) anyCPU = true;
    if (!isNaN(gpuAvgMean)) anyGPU = true;

    // Bound = max(CPU_avg, GPU_avg) si alguno existe; si no, desde FPS
    let boundAvg = NaN;
    if (!isNaN(cpuAvgMean) || !isNaN(gpuAvgMean)){
      const c = isNaN(cpuAvgMean) ? -Infinity : cpuAvgMean;
      const g = isNaN(gpuAvgMean) ? -Infinity : gpuAvgMean;
      boundAvg = Math.max(c, g);
    } else if (!isNaN(fpsAvgMean) && fpsAvgMean > 0) {
      boundAvg = 1000 / fpsAvgMean;
    }

    const fpsFromBound = (!isNaN(boundAvg) && boundAvg > 0) ? 1000 / boundAvg : NaN;
    const headroom     = !isNaN(boundAvg) ? (targetBudget - boundAvg) : NaN;
    const pass         = (!isNaN(fpsFromBound) && fpsFromBound >= targetFps) ? 'YES' : 'NO';
    const notes        = (isNaN(cpuAvgMean) && isNaN(gpuAvgMean)) ? 'Bound estimado desde FPS' : '';

    rowsOut.push([
      name, arr.length,
      safe4(cpuAvgMean), safe4(cpuP95Mean),
      safe4(gpuAvgMean), safe4(gpuP95Mean),
      safe4(boundAvg),   safe2(fpsFromBound), safe3(headroom), pass, notes
    ]);
  });

  // Crear/limpiar hoja resumen
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const outName = sh.getName() + '_Summary';
  const out = ss.getSheetByName(outName) || ss.insertSheet(outName);
  out.clear();

  // Escribir tabla
  out.getRange(1,1,1,summaryHeaders.length).setValues([summaryHeaders]).setFontWeight('bold');
  if (rowsOut.length){
    rowsOut.sort((a,b) => {
      const A = (typeof a[6] === 'number') ? a[6] : Infinity;
      const B = (typeof b[6] === 'number') ? b[6] : Infinity;
      return A - B;
    });
    out.getRange(2,1,rowsOut.length,summaryHeaders.length).setValues(rowsOut);
  }
  out.autoResizeColumns(1, summaryHeaders.length);
  out.setFrozenRows(1);

  // Formato condicional
  if (rowsOut.length){
    const passCol = summaryHeaders.indexOf('Pass') + 1;
    const headCol = summaryHeaders.indexOf('Headroom_ms@'+targetFps) + 1;
    const rules = [
      SpreadsheetApp.newConditionalFormatRule()
        .whenTextEqualTo('YES').setBackground('#C6EFCE').setFontColor('#006100')
        .setRanges([out.getRange(2, passCol, rowsOut.length, 1)]).build(),
      SpreadsheetApp.newConditionalFormatRule()
        .whenTextEqualTo('NO').setBackground('#FFC7CE').setFontColor('#9C0006')
        .setRanges([out.getRange(2, passCol, rowsOut.length, 1)]).build(),
      SpreadsheetApp.newConditionalFormatRule()
        .whenNumberLessThan(0).setBackground('#FCE8B2')
        .setRanges([out.getRange(2, headCol, rowsOut.length, 1)]).build(),
    ];
    out.setConditionalFormatRules(rules);
  }

  // ===== Gráfico 1: Barras Bound (siempre hay) =====
  if (rowsOut.length){
    const last = 1 + rowsOut.length;
    out.insertChart(
      out.newChart()
        .setChartType(Charts.ChartType.BAR)
        .addRange(out.getRange(1, 1, last, 1)) // CaseName
        .addRange(out.getRange(1, 7, last, 1)) // Bound
        .setOption('useFirstRowAsHeaders', true)
        .setOption('legend', { position: 'none' })
        .setOption('title', 'Bound_ms_avg por Caso (↓ mejor)')
        .setPosition(2, summaryHeaders.length + 2, 0, 0)
        .build()
    );

    // ===== Gráfico 2: CPU vs GPU (dinámico, con fallback) =====
    const builder = out.newChart()
      .setChartType(Charts.ChartType.COLUMN)
      .addRange(out.getRange(1, 1, last, 1)) // CaseName
      .setOption('useFirstRowAsHeaders', true)
      .setOption('title', anyCPU || anyGPU ? 'CPU vs GPU avg (ms) por Caso' : 'Bound avg (fallback)');

    if (anyCPU) builder.addRange(out.getRange(1, 3, last, 1)); // CPU
    if (anyGPU) builder.addRange(out.getRange(1, 5, last, 1)); // GPU
    if (!anyCPU && !anyGPU) builder.addRange(out.getRange(1, 7, last, 1)); // Bound si no hay CPU/GPU

    out.insertChart(builder.setPosition(22, summaryHeaders.length + 2, 0, 0).build());
  }

  return out;
}

// -------- FINALIZE MATERIAL EFFICIENCY (nuevo) --------
// Agrupa por CaseName y calcula, por regresión lineal sobre Count (nº de
// instancias, viene del barrido de densidad de BenchmarkCase.densitySweep),
// cuántos ms cuesta CADA instancia extra de CPU y de GPU. Ese "ms/instancia"
// es la métrica que de verdad compara la eficiencia de un packing/shader,
// en vez de un único número absoluto medido a una densidad arbitraria.
function finalizeMaterialEfficiency_(dataSheetName, targetFps) {
  const sh = findValidDataSheet_(dataSheetName);
  const values = sh.getDataRange().getValues();
  if (values.length <= 1) throw new Error('No hay datos para analizar en ' + sh.getName());

  const header = values[0].map(h => String(h).trim());
  const idx = name => header.indexOf(name);

  const iCase = idx('CaseName');
  const iCount = idx('Count');
  const iCPUavg = idx('CPU_ms_avg');
  const iGPUavg = idx('GPU_ms_avg');
  const iBottleneck = idx('Bottleneck');
  const iDept = idx('Department');
  const iPacking = idx('PackingScheme');
  const iTexSamples = idx('TextureSamples');
  const iNotes = idx('VariantNotes');

  if (iCase < 0 || iCount < 0) throw new Error('Faltan columnas CaseName/Count en ' + sh.getName());

  const groups = new Map();
  for (let r = 1; r < values.length; r++) {
    const row = values[r];
    const name = String(row[iCase] || '').trim();
    if (!name) continue;

    const count = toNum(row[iCount]);
    const cpuA = iCPUavg >= 0 ? toNum(row[iCPUavg]) : NaN;
    const gpuA = iGPUavg >= 0 ? toNum(row[iGPUavg]) : NaN;
    if (isNaN(count) || (isNaN(cpuA) && isNaN(gpuA))) continue;

    if (!groups.has(name)) {
      groups.set(name, {
        points: [],
        department: iDept >= 0 ? String(row[iDept] || '') : '',
        packing: iPacking >= 0 ? String(row[iPacking] || '') : '',
        texSamples: iTexSamples >= 0 ? toNum(row[iTexSamples]) : NaN,
        notes: iNotes >= 0 ? String(row[iNotes] || '') : '',
        bottlenecks: {}
      });
    }
    const g = groups.get(name);
    g.points.push({ count, cpuA, gpuA });
    if (iBottleneck >= 0) {
      const b = String(row[iBottleneck] || '').trim();
      if (b) g.bottlenecks[b] = (g.bottlenecks[b] || 0) + 1;
    }
  }

  const summaryHeaders = [
    'CaseName','Department','PackingScheme','TextureSamples','VariantNotes',
    'Points','CPU_ms_per_instance','CPU_fixed_ms','GPU_ms_per_instance','GPU_fixed_ms',
    'DominantBottleneck','Notes'
  ];

  const rowsOut = [];
  groups.forEach((g, name) => {
    const cpuPts = g.points.filter(p => !isNaN(p.cpuA));
    const gpuPts = g.points.filter(p => !isNaN(p.gpuA));

    const cpuReg = linreg_(cpuPts.map(p => p.count), cpuPts.map(p => p.cpuA));
    const gpuReg = linreg_(gpuPts.map(p => p.count), gpuPts.map(p => p.gpuA));

    let dominant = '', maxCount = 0;
    for (const k in g.bottlenecks) if (g.bottlenecks[k] > maxCount) { maxCount = g.bottlenecks[k]; dominant = k; }

    const distinctCounts = new Set(g.points.map(p => p.count)).size;
    const note = distinctCounts < 2
      ? 'Solo 1 densidad: no se puede calcular ms/instancia fiable. Usa densitySweep con >=2 valores.'
      : '';

    rowsOut.push([
      name, g.department, g.packing, isNaN(g.texSamples) ? '' : g.texSamples, g.notes,
      distinctCounts,
      safe4(cpuReg.slope), safe4(cpuReg.intercept),
      safe4(gpuReg.slope), safe4(gpuReg.intercept),
      dominant, note
    ]);
  });

  // Más eficiente primero (menos GPU ms/instancia)
  rowsOut.sort((a,b) => {
    const A = (typeof a[8] === 'number') ? a[8] : Infinity;
    const B = (typeof b[8] === 'number') ? b[8] : Infinity;
    return A - B;
  });

  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const outName = sh.getName() + '_MaterialEfficiency';
  const out = ss.getSheetByName(outName) || ss.insertSheet(outName);
  out.clear();
  out.getRange(1,1,1,summaryHeaders.length).setValues([summaryHeaders]).setFontWeight('bold');
  if (rowsOut.length) out.getRange(2,1,rowsOut.length,summaryHeaders.length).setValues(rowsOut);
  out.autoResizeColumns(1, summaryHeaders.length);
  out.setFrozenRows(1);

  if (rowsOut.length) {
    const last = 1 + rowsOut.length;
    out.insertChart(
      out.newChart()
        .setChartType(Charts.ChartType.BAR)
        .addRange(out.getRange(1, 1, last, 1)) // CaseName
        .addRange(out.getRange(1, 9, last, 1)) // GPU_ms_per_instance
        .setOption('useFirstRowAsHeaders', true)
        .setOption('legend', { position: 'none' })
        .setOption('title', 'GPU ms / instancia por caso (↓ mejor = más eficiente)')
        .setPosition(2, summaryHeaders.length + 2, 0, 0)
        .build()
    );
    out.insertChart(
      out.newChart()
        .setChartType(Charts.ChartType.BAR)
        .addRange(out.getRange(1, 1, last, 1))
        .addRange(out.getRange(1, 7, last, 1)) // CPU_ms_per_instance
        .setOption('useFirstRowAsHeaders', true)
        .setOption('legend', { position: 'none' })
        .setOption('title', 'CPU ms / instancia por caso (↓ mejor = más eficiente)')
        .setPosition(22, summaryHeaders.length + 2, 0, 0)
        .build()
    );
  }

  // Histórico acumulado: nunca se limpia, para comparar entre sesiones de test.
  appendMaterialEfficiencyHistory_(sh.getName(), rowsOut, summaryHeaders);

  return out;
}

// Añade una fila por caso y por ejecución a una hoja persistente
// "MaterialEfficiency_History" (nunca se borra), para poder comparar cómo
// evoluciona la eficiencia de cada packing/shader a lo largo del tiempo.
function appendMaterialEfficiencyHistory_(runLabel, rowsOut, summaryHeaders) {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const histName = 'MaterialEfficiency_History';
  const hist = ss.getSheetByName(histName) || ss.insertSheet(histName);
  const fullHeaders = ['RunLabel','Timestamp'].concat(summaryHeaders);
  if (hist.getLastRow() === 0) hist.appendRow(fullHeaders);

  const now = new Date();
  const out = rowsOut.map(r => [runLabel, now].concat(r));
  if (out.length) hist.getRange(hist.getLastRow()+1, 1, out.length, fullHeaders.length).setValues(out);
}
