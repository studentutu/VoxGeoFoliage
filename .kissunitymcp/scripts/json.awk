# Small strict JSON reader for the Bash transport and repository manifests.
# POSIX awk; no eval, shell execution, or external JSON runtime.
# QUERY is a slash-delimited object/array path. MODE=raw preserves JSON syntax.
function bad() { failed=1; exit 1 }
function ws() { while (substr(s,p,1) ~ /^[ \t\r\n]$/ && p<=length(s)) p++ }
function compact(text, out,i,c,q,escape) {
 for(i=1;i<=length(text);i++) {
  c=substr(text,i,1)
  if(q) { out=out c; if(escape) escape=0; else if(c=="\\") escape=1; else if(c=="\"") q=0 }
  else if(c=="\"") { q=1; out=out c }
  else if(c !~ /[ \t\r\n]/) out=out c
 }
 return out
}
function hex(h, n,i,c) {
 n=0; for(i=1;i<=length(h);i++) { c=index("0123456789abcdef",tolower(substr(h,i,1)))-1; if(c<0) bad(); n=n*16+c } return n
}
function utf(n) {
 if(n==0) bad() # Shell arguments cannot represent NUL; reject, never truncate.
 if(n<128) return sprintf("%c",n)
 if(n<2048) return sprintf("%c%c",192+int(n/64),128+n%64)
 if(n<65536) return sprintf("%c%c%c",224+int(n/4096),128+int(n/64)%64,128+n%64)
 return sprintf("%c%c%c%c",240+int(n/262144),128+int(n/4096)%64,128+int(n/64)%64,128+n%64)
}
function str( out,c,e,n,lo) {
 if(substr(s,p++,1)!="\"") bad()
 out=""
 while(p<=length(s)) {
  c=substr(s,p++,1)
  if(c=="\"") return out
  if(c ~ /[\001-\037]/) bad()
  if(c!="\\") { out=out c; continue }
  e=substr(s,p++,1)
  if(e=="\"" || e=="\\" || e=="/") out=out e
  else if(e=="b") out=out sprintf("%c",8)
  else if(e=="f") out=out sprintf("%c",12)
  else if(e=="n") out=out "\n"
  else if(e=="r") out=out "\r"
  else if(e=="t") out=out "\t"
  else if(e=="u") {
   if(length(substr(s,p,4))!=4) bad()
   n=hex(substr(s,p,4)); p+=4
   if(n>=55296 && n<=56319) {
    if(substr(s,p,2)!="\\u") bad(); p+=2
    lo=hex(substr(s,p,4)); p+=4
    if(lo<56320 || lo>57343) bad()
    n=65536+(n-55296)*1024+lo-56320
   } else if(n>=56320 && n<=57343) bad()
   out=out utf(n)
  } else bad()
 }
 bad()
}
function value(path,depth, start,c,key,n,t,v,idx,token) {
 if(depth>64) bad()
 ws(); start=p; c=substr(s,p,1)
 if(c=="{" || c=="[") {
  t=(c=="{" ? "object" : "array"); p++; ws(); idx=0
  if(substr(s,p,1)!=(c=="{" ? "}" : "]")) {
   while(1) {
    if(c=="{") {
     key=str(); token=path SUBSEP key
     if(token in seen) bad(); seen[token]=1
     ws(); if(substr(s,p++,1)!=":") bad()
     gsub(/~/,"~0",key); gsub(/\//,"~1",key)
    } else key=idx++
    value(path "/" key,depth+1); ws()
    if(substr(s,p,1)!=",") break
    p++; ws()
   }
  }
  if(substr(s,p++,1)!=(c=="{" ? "}" : "]")) bad()
 } else if(c=="\"") { t="string"; v=str() }
 else {
  n=substr(s,p)
  if(match(n,/^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?/)) { t="number"; v=substr(n,1,RLENGTH) }
  else if(substr(n,1,4)=="true" || substr(n,1,4)=="null") { v=substr(n,1,4); t=(v=="null"?"null":"boolean") }
  else if(substr(n,1,5)=="false") { v="false"; t="boolean" }
  else bad()
  p+=length(v)
 }
 if(path==ENVIRON["QUERY"]) {
  found=1; result=(ENVIRON["MODE"]=="raw" ? compact(substr(s,start,p-start)) : v); kind=t
 }
}
{ s=s $0 "\n"; if(length(s)>4194304) bad() }
END {
 if(failed) exit 1
 p=1; value("",0); ws(); if(p<=length(s)) bad()
 if(ENVIRON["MODE"]=="validate") exit 0
 if(!found) exit 2
 if(ENVIRON["MODE"]=="type") print kind
 else print result
}
