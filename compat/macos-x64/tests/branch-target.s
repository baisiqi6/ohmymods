.text
.p2align 4
.globl _branch_target
_branch_target:
 cmpb $0,0x21(%rdi)
 je Lbody
 ret
Lbody:
 incl 0x24(%rdi)
 ret
 .rept 12
 nop
 .endr
 ret
