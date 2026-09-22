.text
.p2align 2
.globl _short_stub
_short_stub:
 b _short_body
.globl _short_body
_short_body:
 mov x0, #7
 ret
.globl _short_getter
_short_getter:
 ldr s0, [x0]
 ret
.globl _short_setter
_short_setter:
 str s0, [x0]
 ret
.globl _short_movtail
_short_movtail:
 mov x2, #0
 b _short_tailbody
.globl _short_tailbody
_short_tailbody:
 add x0, x0, #3
 ret
.globl _adjacent
_adjacent:
 mov x0, #31
 ret
