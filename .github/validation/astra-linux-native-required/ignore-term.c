#include <signal.h>
#include <unistd.h>
int main(void){if(signal(SIGTERM,SIG_IGN)==SIG_ERR)return 70;for(;;)pause();}
