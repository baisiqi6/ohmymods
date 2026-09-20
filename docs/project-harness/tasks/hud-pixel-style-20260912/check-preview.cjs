const fs=require('fs'),vm=require('vm');
const pop={hidden:false,classList:{values:new Set(),toggle(k,v){v?this.values.add(k):this.values.delete(k)}}};
const scene={style:{}};const root={style:{setProperty(k,v){this[k]=v}},querySelector(s){return s==='.kh-pop'?pop:s==='.kh-scene'?scene:null}};
const controls=[];let render;
class Tweak {constructor(opts){render=opts.onChange}addToggle(o,k,x){controls.push({o,k})}addSelect(o,k,x){controls.push({o,k})}}
vm.runInNewContext(fs.readFileSync(__dirname+'/preview-script.js','utf8'),{document:{getElementById(id){if(id!=='kingdom-hud-layout')throw Error('wrong root');return root}},Tweak});
function check(x,m){if(!x)throw Error(m)}
check(pop.hidden,'population hidden by default as user requested');
const state=controls[0].o;state.showPopulation=true;state.layout='edge';render();
check(!pop.hidden&&pop.classList.values.has('kh-lshape')&&scene.style.minHeight==='405px','future layout control updates');
state.border='copper';render();check(root.style['--kh-edge']==='#b1965e','edge style control updates');
state.showPopulation=false;render();check(pop.hidden&&scene.style.minHeight==='','current scope restores');
console.log('PASS 4 preview state/interaction checks');
