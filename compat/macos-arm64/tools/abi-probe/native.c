void *identity(void *p) { return p ? p : (void*)0x1234; }
void *call_one(void *fn,void *arg) { return ((void*(*)(void*))fn)(arg); }
