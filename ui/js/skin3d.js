/* Minecraft skin viewer. Requires Three.js r160 and skin3d-core.js. */
(function () {
	'use strict';
	var Core = window.Skin3DCore;
	var COLORS = { head: '#b5835a', body: '#3aa0a0', arm: '#b5835a', leg: '#3a4a80' };

	function paletteCanvas(colors) {
		var c = document.createElement('canvas'), g = c.getContext('2d'), p = Object.assign({}, COLORS, colors || {}); c.width = 64; c.height = 64;
		function fill(x, y, w, h, color) { g.fillStyle = color; g.fillRect(x, y, w, h); }
		fill(8,0,16,8,p.head); fill(0,8,32,8,p.head); fill(20,16,20,4,p.body); fill(16,20,24,12,p.body);
		fill(44,16,16,4,p.arm); fill(40,20,16,12,p.arm); fill(36,48,16,4,p.arm); fill(32,52,16,12,p.arm);
		fill(4,16,8,4,p.leg); fill(0,20,16,12,p.leg); fill(20,48,8,4,p.leg); fill(16,52,16,12,p.leg); return c;
	}
	function crop(source, region) { var c = document.createElement('canvas'); c.width=region[2]; c.height=region[3]; c.getContext('2d').drawImage(source,region[0],region[1],region[2],region[3],0,0,region[2],region[3]); return c; }
	function disposeObject(object) {
		if (!object) return; object.traverse(function (child) {
			if (child.geometry) child.geometry.dispose(); var mats = child.material ? (Array.isArray(child.material) ? child.material : [child.material]) : [];
			mats.forEach(function (mat) { Object.keys(mat).forEach(function (key) { if (mat[key] && mat[key].isTexture) mat[key].dispose(); }); mat.dispose(); });
		});
	}
	function addBox(parent, source, w, h, d, map, overlay) {
		if (!map) return; var group = new THREE.Group();
		function face(region, rotation, position, pw, ph) {
			var tex = new THREE.CanvasTexture(crop(source, region)); tex.colorSpace=THREE.SRGBColorSpace; tex.magFilter=THREE.NearestFilter; tex.minFilter=THREE.NearestFilter;
			var mat = new THREE.MeshLambertMaterial({map:tex,transparent:!!overlay,alphaTest:overlay?.01:0,side:THREE.FrontSide});
			var mesh = new THREE.Mesh(new THREE.PlaneGeometry(pw,ph),mat); mesh.rotation.set.apply(mesh.rotation,rotation); mesh.position.set.apply(mesh.position,position); group.add(mesh);
		}
		face(map.top,[-Math.PI/2,0,0],[0,h/2,0],w,d); face(map.bottom,[Math.PI/2,0,0],[0,-h/2,0],w,d);
		face(map.front,[0,0,0],[0,0,d/2],w,h); face(map.back,[0,Math.PI,0],[0,0,-d/2],w,h);
		face(map.right,[0,Math.PI/2,0],[w/2,0,0],d,h); face(map.left,[0,-Math.PI/2,0],[-w/2,0,0],d,h);
		if (overlay) group.scale.setScalar(1.035); parent.add(group);
	}

	function Skin3DViewer(container, options) {
		if (!Core || typeof THREE === 'undefined') throw new Error('WebGL 3D component is unavailable');
		this.container=container; this.options=options||{}; this.gate=new Core.RequestGate(); this.root=new THREE.Group(); this.clock=new THREE.Clock();
		this.autoRotate=!matchMedia('(prefers-reduced-motion: reduce)').matches; this.listeners=[]; this.running=false; this.disposed=false; this.source=paletteCanvas(); this.modelType='classic';
		this.initRenderer(); this.initLights(); this.buildModel(); this.initControls(); this.start();
	}
	Skin3DViewer.prototype.listen=function(target,type,fn,options){target.addEventListener(type,fn,options);this.listeners.push([target,type,fn,options]);};
	Skin3DViewer.prototype.initRenderer=function(){
		this.renderer=new THREE.WebGLRenderer({antialias:true,alpha:true}); this.renderer.setClearColor(0,0); this.renderer.setPixelRatio(Math.min(devicePixelRatio||1,2));
		this.canvas=this.renderer.domElement; this.canvas.className='skin3d-canvas'; this.canvas.tabIndex=0; this.canvas.setAttribute('role','img'); this.canvas.setAttribute('aria-label',this.options.ariaLabel||'3D Minecraft skin preview'); this.container.appendChild(this.canvas);
		this.scene=new THREE.Scene(); this.scene.add(this.root); this.camera=new THREE.PerspectiveCamera(34,1,1,400); this.camera.position.set(0,16,78); this.camera.lookAt(0,15,0); this.scene.add(this.camera);
		var self=this; this.resizeObserver=new ResizeObserver(function(){self.resize();}); this.resizeObserver.observe(this.container); this.resize();
		this.listen(this.canvas,'webglcontextlost',function(e){e.preventDefault();self.stop();self.error('WebGL context lost');});
		this.listen(this.canvas,'webglcontextrestored',function(){self.start();self.ready();self.setSkin(self.lastSkin,self.lastAccountId);});
	};
	Skin3DViewer.prototype.initLights=function(){this.scene.add(new THREE.AmbientLight(0xffffff,1));var a=new THREE.DirectionalLight(0xffffff,.9);a.position.set(50,80,120);this.scene.add(a);var b=new THREE.DirectionalLight(0xb0c4ff,.45);b.position.set(-50,30,80);this.scene.add(b);};
	Skin3DViewer.prototype.part=function(parent,x,y,w,h,d,base,overlay){var pivot=new THREE.Group();pivot.position.set(x,y,0);parent.add(pivot);var center=new THREE.Group();center.position.y=-h/2;pivot.add(center);addBox(center,this.source,w,h,d,base,false);addBox(center,this.source,w,h,d,overlay,true);return pivot;};
	Skin3DViewer.prototype.buildModel=function(){
		var m=Core.regions(this.modelType,this.source.height===32),armW=this.modelType==='slim'?3:4,gap=4+armW/2; this.model=new THREE.Group();this.root.add(this.model);
		this.headPivot=new THREE.Group();this.headPivot.position.set(0,28,0);this.model.add(this.headPivot);addBox(this.headPivot,this.source,8,8,8,m.head,false);addBox(this.headPivot,this.source,8.5,8.5,8.5,m.hat,true);
		var body=new THREE.Group();body.position.set(0,18,0);this.model.add(body);addBox(body,this.source,8,12,4,m.body,false);addBox(body,this.source,8,12,4,m.bodyOverlay,true);
		this.armR=this.part(this.model,-gap,24,armW,12,4,m.rarm,m.rarmOverlay);this.armL=this.part(this.model,gap,24,armW,12,4,m.larm,m.larmOverlay);
		this.legR=this.part(this.model,-2,12,4,12,4,m.rleg,m.rlegOverlay);this.legL=this.part(this.model,2,12,4,12,4,m.lleg,m.llegOverlay);this.model.scale.setScalar(1.15);
	};
	Skin3DViewer.prototype.rebuild=function(){if(this.model){this.root.remove(this.model);disposeObject(this.model);}this.model=null;this.buildModel();};
	Skin3DViewer.prototype.initControls=function(){
		var self=this,drag=false,x=0,y=0;function finish(e){drag=false;self.canvas.classList.remove('dragging');if(e&&self.canvas.hasPointerCapture(e.pointerId))self.canvas.releasePointerCapture(e.pointerId);}
		this.listen(this.canvas,'pointerdown',function(e){drag=true;x=e.clientX;y=e.clientY;self.autoRotate=false;self.canvas.setPointerCapture(e.pointerId);self.canvas.classList.add('dragging');});
		this.listen(this.canvas,'pointermove',function(e){if(!drag)return;self.root.rotation.y+=(e.clientX-x)*.008;self.root.rotation.x=Math.max(-.9,Math.min(.9,self.root.rotation.x+(e.clientY-y)*.006));x=e.clientX;y=e.clientY;});
		this.listen(this.canvas,'pointerup',finish);this.listen(this.canvas,'pointercancel',finish);this.listen(this.canvas,'lostpointercapture',finish);
		this.listen(this.canvas,'wheel',function(e){e.preventDefault();self.zoom(e.deltaY*.05);},{passive:false});
		this.listen(this.canvas,'keydown',function(e){var used=true;if(e.key==='ArrowLeft')self.root.rotation.y-=.12;else if(e.key==='ArrowRight')self.root.rotation.y+=.12;else if(e.key==='ArrowUp')self.root.rotation.x-=.1;else if(e.key==='ArrowDown')self.root.rotation.x+=.1;else if(e.key==='+'||e.key==='=')self.zoom(-4);else if(e.key==='-')self.zoom(4);else if(e.key==='0'||e.key==='Home')self.resetView();else used=false;if(used){e.preventDefault();self.autoRotate=false;}});
		this.listen(document,'visibilitychange',function(){if(document.hidden)self.stop();else self.start();});
	};
	Skin3DViewer.prototype.zoom=function(amount){this.camera.position.z=Math.max(34,Math.min(95,this.camera.position.z+amount));};
	Skin3DViewer.prototype.resetView=function(){this.root.rotation.set(0,0,0);this.camera.position.z=78;};
	Skin3DViewer.prototype.setAutoRotate=function(value){this.autoRotate=!!value&&!matchMedia('(prefers-reduced-motion: reduce)').matches;};
	Skin3DViewer.prototype.start=function(){if(this.running||this.disposed||document.hidden)return;this.running=true;this.clock.start();var self=this;function tick(){if(!self.running||self.disposed)return;var t=self.clock.getElapsedTime();if(self.autoRotate)self.root.rotation.y+=.0045;if(self.armR)self.armR.rotation.x=Math.sin(t*1.6)*.09;if(self.armL)self.armL.rotation.x=-Math.sin(t*1.6)*.09;if(self.headPivot)self.headPivot.rotation.z=Math.sin(t*.8)*.035;self.renderer.render(self.scene,self.camera);self.raf=requestAnimationFrame(tick);}this.raf=requestAnimationFrame(tick);};
	Skin3DViewer.prototype.stop=function(){if(!this.running)return;this.running=false;cancelAnimationFrame(this.raf);this.raf=0;this.clock.stop();};
	Skin3DViewer.prototype.resize=function(){if(this.disposed)return;var w=this.container.clientWidth||260,h=this.container.clientHeight||300;this.renderer.setSize(w,h,false);this.camera.aspect=w/Math.max(h,1);this.camera.updateProjectionMatrix();};
	Skin3DViewer.prototype.error=function(message,url){if(this.options.onError)this.options.onError(message,url||'');}; Skin3DViewer.prototype.ready=function(){if(this.options.onReady)this.options.onReady();};
	Skin3DViewer.prototype.setSkin=function(skin,accountId){
		this.lastSkin=skin;this.lastAccountId=accountId;var self=this,seq=this.gate.next(),url=skin&&skin.url||'',proxy=accountId?'/api/accounts/'+encodeURIComponent(accountId)+'/skin-image?token='+encodeURIComponent(window.SG&&SG.apiToken||''):'';this.modelType=Core.normalizeModel(skin&&skin.model);var attempts=[];if(proxy)attempts.push(proxy);else if(url)attempts.push(url);
		function next(i){if(!self.gate.accepts(seq))return;if(i>=attempts.length){self.error(window.SG&&SG.t?SG.t('skin.textureUnavailable'):'Skin texture unavailable',proxy);return;}var image=new Image();image.crossOrigin='anonymous';image.onload=function(){if(!self.gate.accepts(seq))return;if(!Core.validDimensions(image.naturalWidth,image.naturalHeight)){next(i+1);return;}self.source=image;self.rebuild();self.ready();};image.onerror=function(){if(self.gate.accepts(seq))next(i+1);};image.src=attempts[i];}next(0);
	};
	Skin3DViewer.prototype.dispose=function(){if(this.disposed)return;this.disposed=true;this.gate.dispose();this.stop();if(this.resizeObserver)this.resizeObserver.disconnect();this.listeners.forEach(function(x){x[0].removeEventListener(x[1],x[2],x[3]);});this.listeners=[];if(this.model){this.root.remove(this.model);disposeObject(this.model);this.model=null;}if(this.renderer){this.renderer.renderLists.dispose();this.renderer.dispose();if(this.renderer.forceContextLoss)this.renderer.forceContextLoss();}if(this.canvas&&this.canvas.parentNode)this.canvas.parentNode.removeChild(this.canvas);};
	window.Skin3DViewer=Skin3DViewer;
})();
