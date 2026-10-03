#include <signal.h>
#include <unistd.h>
int main(int argc,char**argv){if(argc<2)return 64;if(signal(SIGCHLD,SIG_IGN)==SIG_ERR)return 70;execv(argv[1],argv+1);return 71;}
