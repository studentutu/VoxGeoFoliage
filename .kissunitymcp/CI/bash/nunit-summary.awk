# Validate the XML structure emitted by Unity/NUnit before trusting its summary.
# Intentionally reject DTDs; never resolve external entities. POSIX awk.
function die() { failed=1; exit 1 }
function attributes(text,root, key,q,val) {
 while(text !~ /^[ \t\r\n]*$/) {
  sub(/^[ \t\r\n]+/,"",text)
  if(!match(text,/^[A-Za-z_:][A-Za-z0-9_.:-]*/)) die()
  key=substr(text,1,RLENGTH); text=substr(text,RLENGTH+1)
  if(seenAttribute[key]++) die()
  sub(/^[ \t\r\n]*/,"",text); if(substr(text,1,1)!="=") die()
  text=substr(text,2); sub(/^[ \t\r\n]*/,"",text)
  q=substr(text,1,1); if(q!="\"" && q!="\047") die()
  text=substr(text,2); if(!index(text,q)) die()
  val=substr(text,1,index(text,q)-1); text=substr(text,index(text,q)+1)
  if(val ~ /</) die()
  if(root) attr[key]=val
  if(length(text) && text !~ /^[ \t\r\n]/) die()
 }
 for(key in seenAttribute) delete seenAttribute[key]
}
{ xml=xml $0 "\n" }
END {
 if(failed) exit 1
 while(length(xml)) {
  if(substr(xml,1,1)!="<") {
   n=index(xml,"<"); if(!n) n=length(xml)+1
   text=substr(xml,1,n-1)
   if(depth==0 && text !~ /^[ \t\r\n]*$/) die()
   xml=substr(xml,n); continue
  }
  if(substr(xml,1,4)=="<!--") { n=index(xml,"-->"); if(!n) die(); xml=substr(xml,n+3); continue }
  if(substr(xml,1,9)=="<![CDATA[") { if(!depth) die(); n=index(xml,"]]>"); if(!n) die(); xml=substr(xml,n+3); continue }
  if(substr(xml,1,2)=="<?") { n=index(xml,"?>"); if(!n) die(); xml=substr(xml,n+2); continue }
  if(substr(xml,1,2)=="<!") die()
  q=""; n=2
  for(;n<=length(xml);n++) {
   c=substr(xml,n,1)
   if(q!="") { if(c==q) q="" }
   else if(c=="\"" || c=="\047") q=c
   else if(c==">") break
  }
  if(n>length(xml) || q!="") die()
  tag=substr(xml,2,n-2); xml=substr(xml,n+1)
  closing=(substr(tag,1,1)=="/"); if(closing) tag=substr(tag,2)
  empty=(tag ~ /\/$/); if(empty) tag=substr(tag,1,length(tag)-1)
  if(!match(tag,/^[A-Za-z_:][A-Za-z0-9_.:-]*/)) die()
  name=substr(tag,1,RLENGTH); rest=substr(tag,RLENGTH+1)
  if(closing) {
   if(empty || rest !~ /^[ \t\r\n]*$/ || !depth || stack[depth]!=name) die()
   depth--; continue
  }
  if(rest!="" && rest !~ /^[ \t\r\n]/) die()
  if(!depth) { if(rootSeen++ || name!="test-run") die(); attributes(rest,1) }
  else attributes(rest,0)
  if(name=="test-case") cases++
  if(!empty) stack[++depth]=name
 }
 if(depth || !rootSeen) die()
 split("total passed failed inconclusive skipped",keys," ")
 for(i=1;i<=5;i++) if(attr[keys[i]] !~ /^[0-9]+$/) die()
 if(attr["total"]+0 != cases+0 || attr["total"]+0 != attr["passed"]+attr["failed"]+attr["inconclusive"]+attr["skipped"]) die()
 if(attr["result"] !~ /^(Passed|Failed|Inconclusive|Skipped)(\([A-Za-z]+\))?$/) die()
 # Unity Test Framework 1.6 emits composite labels such as Failed(Child).
 sub(/\(.*/, "", attr["result"])
 printf "<test-run result=\"%s\"",attr["result"]
 for(i=1;i<=5;i++) printf " %s=\"%s\"",keys[i],attr[keys[i]]
 print ">"
}
