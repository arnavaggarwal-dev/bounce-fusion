$msg = Read-Host -Prompt "Input commit msg:"   
git add .
git commit -m $msg
git push 