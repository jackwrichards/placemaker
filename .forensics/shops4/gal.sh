#!/bin/sh
# gal.sh <outdir> <types> <presets> <sites> [markers]: offline renders + facts
out=$(cygpath -m "$1"); types=$2; presets=$3; sites=$4; markers=${5:-entrance@s:0.06,entrance@e:0.92,footpath@w:0.5}
rm -rf $out; mkdir -p $out
dotnet run -c Release --no-build --project Tests/Placemaker.Gallery -- --out $out --only $types --presets $presets --sites $sites --markers "$markers" > $out/run.log 2>$out/err.log
node -e "
const g=JSON.parse(require('fs').readFileSync('$out/gallery.json','utf8'));for(const x of (g.items||g)){if(!/preset/.test(x.variant))continue;const f=x.facts||{};
console.log(x.type, x.variant.replace('-preset',''), 'plots',f.shopPlots,'lots',f.lots,'cap',f.capacity,'edges',Math.round(f.lotEdges||0),'failM2',f.restLotFailM2||0,'gates',f.gatesLanded+'/'+f.gates, 'nets',x.nets,'err',x.errors,'warn',x.warnings,(x.issues||[]).filter(i=>!/Paint.Issue.Permanent/.test(i)).join(' '))}"
