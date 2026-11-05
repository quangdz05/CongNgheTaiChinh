import fs from 'node:fs';
import path from 'node:path';
import {createRequire} from 'node:module';
const root=path.resolve(import.meta.dirname,'../Front-end');
const require=createRequire(path.join(root,'package.json'));
const ts=require('typescript');
const config=ts.readConfigFile(path.join(root,'tsconfig.json'),ts.sys.readFile);
const parsed=ts.parseJsonConfigFileContent(config.config,ts.sys,root);
const host={getScriptFileNames:()=>parsed.fileNames,getScriptVersion:()=> '1',getScriptSnapshot:file=>fs.existsSync(file)?ts.ScriptSnapshot.fromString(fs.readFileSync(file,'utf8')):undefined,getCurrentDirectory:()=>root,getCompilationSettings:()=>parsed.options,getDefaultLibFileName:o=>ts.getDefaultLibFilePath(o),fileExists:ts.sys.fileExists,readFile:ts.sys.readFile,readDirectory:ts.sys.readDirectory};
const service=ts.createLanguageService(host);
for(const file of parsed.fileNames){
  const changes=service.organizeImports({type:'file',fileName:file},{},{});
  for(const change of changes){let text=fs.readFileSync(change.fileName,'utf8');for(const edit of [...change.textChanges].sort((a,b)=>b.span.start-a.span.start))text=text.slice(0,edit.span.start)+edit.newText+text.slice(edit.span.start+edit.span.length);fs.writeFileSync(change.fileName,text);}
}
console.log('Organized TypeScript imports.');
