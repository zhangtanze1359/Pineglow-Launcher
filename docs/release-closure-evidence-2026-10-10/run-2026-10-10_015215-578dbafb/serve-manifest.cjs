// 本地清单服务器（第 59 轮）：用 **HTTP** 提供 update.json，并在日志里记录每一次请求。
// 用途：查清"清单地址是否允许 HTTP""产物地址是否强制 HTTPS""下载→校验→替换→重启"这条链能否在本地验通。
// 用法：node serve-manifest.cjs <监听端口> <manifest.json 路径> <请求日志路径>
const http = require('http');
const fs = require('fs');

const port = Number(process.argv[2]);
const manifestPath = process.argv[3];
const logPath = process.argv[4];
function log(line) { try { fs.appendFileSync(logPath, new Date().toISOString() + '  ' + line + '\n'); } catch (e) { } }

const server = http.createServer((req, res) => {
  log(`${req.method} ${req.url}`);
  if (req.url.startsWith('/manifest.json')) {
    let body = '';
    try { body = fs.readFileSync(manifestPath, 'utf8'); } catch (e) { res.writeHead(500); res.end('no manifest'); return; }
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(body);
    return;
  }
  res.writeHead(404, { 'Content-Type': 'text/plain' });
  res.end('not found');
});
server.listen(port, '127.0.0.1', () => log(`LISTENING ${port}`));
