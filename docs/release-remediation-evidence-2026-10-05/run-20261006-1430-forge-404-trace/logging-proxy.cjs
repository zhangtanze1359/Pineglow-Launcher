// 记录型转发代理（第 52 轮）：把启动器的请求原样转发，同时把「方法 + 目标」写进日志。
// 用途：查清"Forge 安装器 404"时启动器**真正请求的是哪个地址**（官方 maven 还是某个镜像）。
// 用法：node logging-proxy.cjs <日志文件> <端口>
// 说明：HTTPS 走 CONNECT 隧道 —— 能看到**主机名**（足以区分官方/镜像），但看不到路径（不做 MITM）。
const http = require('http');
const net = require('net');
const fs = require('fs');

const logPath = process.argv[2];
const port = Number(process.argv[3]);
function log(line) {
  try { fs.appendFileSync(logPath, new Date().toISOString() + '  ' + line + '\n'); } catch (e) { }
}

const server = http.createServer((req, res) => {
  let host = '';
  try { host = new URL(req.url).host; } catch (e) { }
  log(`HTTP  ${req.method} ${req.url}`);
  let target;
  try { target = new URL(req.url); } catch (e) { res.writeHead(400); res.end('bad url'); return; }
  const opts = {
    host: target.hostname,
    port: target.port || 80,
    path: target.pathname + target.search,
    method: req.method,
    headers: req.headers
  };
  const upstream = http.request(opts, (r) => {
    res.writeHead(r.statusCode, r.headers);
    r.pipe(res);
  });
  upstream.on('error', (e) => { log('HTTP-ERR ' + host + ' ' + e.message); try { res.writeHead(502); res.end('proxy error'); } catch (x) { } });
  req.pipe(upstream);
});

server.on('connect', (req, clientSocket, head) => {
  const target = req.url;                       // host:port
  const [host, p] = target.split(':');
  log(`CONNECT ${target}`);
  const upstream = net.connect(Number(p || 443), host, () => {
    clientSocket.write('HTTP/1.1 200 Connection Established\r\n\r\n');
    upstream.write(head);
    upstream.pipe(clientSocket);
    clientSocket.pipe(upstream);
  });
  upstream.on('error', (e) => { log('CONNECT-ERR ' + target + ' ' + e.message); try { clientSocket.end(); } catch (x) { } });
  clientSocket.on('error', () => { try { upstream.end(); } catch (x) { } });
});

server.listen(port, '127.0.0.1', () => log('PROXY listening on 127.0.0.1:' + port));
