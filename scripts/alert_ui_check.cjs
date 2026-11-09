// Run via ALERT_UI_CHECK=1 python scripts/alert_check.py; uses only that script's temporary API.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const os = require('node:os');
const moduleRoot = process.env.CODEX_NODE_MODULES || path.join(os.homedir(), '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules');
const { chromium } = require(require.resolve('playwright', { paths: [moduleRoot] }));
const root = path.resolve(__dirname, '..');
const dist = path.join(root, 'Front-end/dist');
const apiUrl = process.env.ALERT_TEST_API_URL;
if (!apiUrl || !/^http:\/\/127\.0\.0\.1:\d+$/.test(apiUrl)) throw new Error('A disposable local API URL from alert_check.py is required');
const target = new URL(apiUrl);
const server = http.createServer((req, res) => {
  if (req.url.startsWith('/api/') || req.url === '/health') {
    const proxy = http.request({ hostname: target.hostname, port: target.port, path: req.url, method: req.method, headers: req.headers }, incoming => { res.writeHead(incoming.statusCode, incoming.headers); incoming.pipe(res); });
    proxy.on('error', () => { res.writeHead(502); res.end(); }); req.pipe(proxy); return;
  }
  const filename = path.resolve(dist, '.' + new URL(req.url, 'http://localhost').pathname);
  if (!filename.startsWith(dist + path.sep) && filename !== dist) { res.writeHead(403); res.end(); return; }
  const file = fs.existsSync(filename) && fs.statSync(filename).isFile() ? filename : path.join(dist, 'index.html');
  const mime = { '.js':'text/javascript', '.css':'text/css', '.html':'text/html' };
  res.writeHead(200, { 'Content-Type':mime[path.extname(file)] || 'application/octet-stream' }); fs.createReadStream(file).pipe(res);
});

(async () => {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const origin = `http://127.0.0.1:${server.address().port}`;
  const browser = await chromium.launch({ channel: process.env.ALERT_BROWSER_CHANNEL || 'msedge', headless:true });
  const errors = [];
  const output = path.join(root, 'test-results/alerts'); fs.mkdirSync(output, { recursive:true });
  let currentPage;
  try {
    const loginResponse = await fetch(apiUrl+'/api/auth/login', { method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({username:'admin', password:'Admin@123456'}) });
    const { token } = await loginResponse.json();
    const request = async (url, body) => {
      const response = await fetch(apiUrl+'/api'+url, { method:body ? 'POST':'GET', headers:{ Authorization:'Bearer '+token, 'Content-Type':'application/json' }, ...(body ? {body:JSON.stringify(body)} : {}) });
      const result = await response.json(); assert.equal(response.status,200,JSON.stringify(result)); return result;
    };
    const tx = await request('/transactions', { id:crypto.randomUUID(), userId:'UI-CHECK', amount:150000000, currency:'VND', merchant:'UI investigation', category:'grocery', country:'VN', deviceId:crypto.randomUUID(), ipAddress:'192.0.2.60', timestamp:'2026-01-02T12:00:00Z', mode:'RulesOnly' });
    let alert = (await request('/alerts?search='+tx.id)).items[0];
    const context = await browser.newContext({ viewport:{width:1440,height:1000} });
    const page = await context.newPage(); currentPage = page; page.on('pageerror', e => errors.push(e.message));
    await page.goto(origin);
    await page.getByLabel('Tên đăng nhập').fill('admin'); await page.getByLabel('Mật khẩu').fill('Admin@123456');
    await page.getByRole('button', {name:'Đăng nhập',exact:true}).click();
    await page.getByRole('button', {name:/Hàng đợi cảnh báo/}).click();
    await page.getByLabel('Tìm cảnh báo').fill(tx.id);
    await page.getByRole('button', {name:new RegExp('UI-CHECK')}).click();
    await page.getByRole('dialog').waitFor();
    const operation = async name => {
      await page.getByLabel('Thao tác', {exact:true}).selectOption(name);
      const pending = page.waitForResponse(r=>r.url().endsWith('/'+name) && r.request().method()==='POST');
      await page.getByRole('dialog').getByRole('button', { name:({claim:'Nhận việc',start:'Bắt đầu điều tra',close:'Đóng cảnh báo',notes:'Thêm ghi chú'})[name], exact:true }).click();
      const response = await pending; assert.equal(response.status(),200,await response.text());
      alert = (await response.json()).alert;
    };
    await operation('claim');
    await page.getByLabel('Thao tác', {exact:true}).locator('option[value="start"]').waitFor({state:'attached'});
    await operation('start');
    await page.getByLabel('Thao tác', {exact:true}).locator('option[value="notes"]').waitFor({state:'attached'});
    await page.getByLabel('Thao tác', {exact:true}).selectOption('notes');
    await page.getByLabel('Lý do / ghi chú').fill('<script>window.alertXss=1</script>');
    // Focus refresh must preserve draft text and not submit it.
    const refresh = page.waitForResponse(r=>r.url().endsWith('/api/alerts/'+alert.id) && r.request().method()==='GET');
    await page.evaluate(()=>window.dispatchEvent(new Event('focus'))); await refresh;
    assert.equal(await page.getByLabel('Lý do / ghi chú').inputValue(),'<script>window.alertXss=1</script>');
    await context.setOffline(true);
    await page.evaluate(()=>window.dispatchEvent(new Event('offline')));
    await page.getByRole('dialog').getByText('· Dữ liệu có thể đã cũ',{exact:true}).waitFor();
    await context.setOffline(false);
    await page.evaluate(()=>window.dispatchEvent(new Event('focus')));
    await page.waitForFunction(()=>!document.querySelector('.alert-dialog .alert-sync strong'));
    await operation('notes');
    assert.equal(await page.evaluate(()=>window.alertXss),undefined);
    await page.getByRole('dialog').getByText('<script>window.alertXss=1</script>', {exact:true}).waitFor();
    await page.getByLabel('Thao tác', {exact:true}).selectOption('close');
    await page.getByLabel('Căn cứ / lý do đóng').fill('Chưa có đủ bằng chứng để kết luận');
    assert.equal(await page.getByLabel('Kết luận và lý do').inputValue(),'InsufficientEvidence');
    await page.screenshot({path:path.join(output,'alert-desktop.png'),fullPage:true});
    await operation('close'); assert.equal(alert.resolution,'Unknown');
    await page.getByRole('dialog').getByText('Lý do đóng: Chưa có đủ bằng chứng để kết luận').waitFor();
    await page.setViewportSize({width:390,height:844});
    await page.screenshot({path:path.join(output,'alert-mobile.png'),fullPage:true});
    assert.equal(await page.evaluate(()=>document.querySelector('.alert-dialog').getBoundingClientRect().width <= innerWidth),true);
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth <= innerWidth),true);
    await page.getByRole('button',{name:'Đóng cửa sổ',exact:true}).click();
    await page.getByLabel('Trạng thái cảnh báo').selectOption('Closed');
    await page.getByRole('button', {name:new RegExp('UI-CHECK')}).waitFor();
    assert.deepEqual(errors,[]);
    console.log('Alert UI passed: login, queue, claim/start/note, refresh preserves draft, XSS escaping, explicit Unknown closure, desktop/mobile.');
  } catch (error) {
    if (currentPage) {
      await currentPage.screenshot({path:path.join(output,'alert-ui-failure.png'),fullPage:true});
      fs.writeFileSync(path.join(output,'alert-ui-failure.txt'),await currentPage.locator('body').innerText());
    }
    console.error('Browser runtime errors:',errors);
    throw error;
  } finally { await browser.close(); server.closeAllConnections(); await new Promise(resolve=>server.close(resolve)); }
})().catch(error=>{ console.error(error); server.close(); process.exitCode=1; });
