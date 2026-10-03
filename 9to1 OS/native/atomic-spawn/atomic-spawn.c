#define _GNU_SOURCE
#include <errno.h>
#include <fcntl.h>
#include <linux/sched.h>
#include <poll.h>
#include <signal.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/prctl.h>
#include <sys/socket.h>
#include <sys/syscall.h>
#include <sys/un.h>
#include <sys/wait.h>
#include <time.h>
#include <unistd.h>
extern char **environ;
enum { CREATED=1,GO=2,STARTED=3,FAILED=4,EXITED=5,STOP=6 };
static unsigned char nonce[32];
static int control=-1, childfd=-1;
static pid_t child;
static uint64_t ticks;
static void put(unsigned char *p,uint64_t v,int n){for(int i=0;i<n;i++)p[i]=(unsigned char)(v>>(8*i));}
static uint64_t get(const unsigned char*p,int n){uint64_t v=0;for(int i=0;i<n;i++)v|=(uint64_t)p[i]<<(8*i);return v;}
static int packet(int kind,int seq,unsigned error,int transfer){
 unsigned char b[64]={0};put(b,0x31505341,4);put(b+4,1,2);put(b+6,kind,2);put(b+8,seq,8);memcpy(b+16,nonce,32);put(b+48,(unsigned)child,4);put(b+52,error,4);put(b+56,ticks,8);
 struct iovec io={b,sizeof b};char ancillary[CMSG_SPACE(sizeof(int))]={0};struct msghdr m={.msg_iov=&io,.msg_iovlen=1};
 if(transfer){m.msg_control=ancillary;m.msg_controllen=sizeof ancillary;struct cmsghdr*c=CMSG_FIRSTHDR(&m);c->cmsg_level=SOL_SOCKET;c->cmsg_type=SCM_RIGHTS;c->cmsg_len=CMSG_LEN(sizeof(int));memcpy(CMSG_DATA(c),&childfd,sizeof childfd);}
 return sendmsg(control,&m,MSG_NOSIGNAL)==64?0:-1;
}
static int receive(int kind,int seq){
 unsigned char b[65];char ancillary[4096];struct iovec io={b,sizeof b};struct msghdr m={.msg_iov=&io,.msg_iovlen=1,.msg_control=ancillary,.msg_controllen=sizeof ancillary};
 ssize_t n=recvmsg(control,&m,MSG_CMSG_CLOEXEC);int bad=0;
 for(struct cmsghdr*c=CMSG_FIRSTHDR(&m);c;c=CMSG_NXTHDR(&m,c)){bad=1;if(c->cmsg_level==SOL_SOCKET&&c->cmsg_type==SCM_RIGHTS&&c->cmsg_len>=CMSG_LEN(0)){size_t count=(c->cmsg_len-CMSG_LEN(0))/sizeof(int);int *fds=(int*)CMSG_DATA(c);for(size_t i=0;i<count;i++)close(fds[i]);}}
 return n==64&&!bad&&!(m.msg_flags&(MSG_TRUNC|MSG_CTRUNC))&&get(b,4)==0x31505341&&get(b+4,2)==1&&get(b+6,2)==(unsigned)kind&&get(b+8,8)==(unsigned)seq&&!memcmp(b+16,nonce,32)&&get(b+48,4)==0&&get(b+52,4)==0&&get(b+56,8)==0?0:-1;
}
static int64_t now(void){struct timespec t;if(clock_gettime(CLOCK_MONOTONIC,&t))return -1;return (int64_t)t.tv_sec*1000+t.tv_nsec/1000000;}
static int ready(int fd,int64_t deadline){for(;;){int64_t rem=deadline-now();if(rem<=0)return 0;struct pollfd p={fd,POLLIN,0};int r=poll(&p,1,(int)rem);if(r<0&&errno==EINTR)continue;return r>0?1:r;}}
static int reap(siginfo_t*info,int timeout){int r=ready(childfd,now()+timeout);if(r<=0)return -1;do{r=waitid(P_PIDFD,(id_t)childfd,info,WEXITED);}while(r<0&&errno==EINTR);return r;}
static int drain(siginfo_t*info){
 if(syscall(SYS_pidfd_send_signal,childfd,SIGTERM,NULL,0)<0&&errno!=ESRCH)return -1;
 if(!reap(info,10000))return 0;
 if(syscall(SYS_pidfd_send_signal,childfd,SIGKILL,NULL,0)<0&&errno!=ESRCH)return -1;
 return reap(info,10000);
}
static uint64_t start_ticks(pid_t pid){char path[64],b[4096];snprintf(path,sizeof path,"/proc/%d/stat",pid);int fd=open(path,O_RDONLY|O_CLOEXEC);if(fd<0)return 0;ssize_t n=read(fd,b,sizeof b-1);close(fd);if(n<=0)return 0;b[n]=0;char*p=strrchr(b,')');if(!p||p[1]!=' ')return 0;p+=2;for(int field=3;field<22;field++){p=strchr(p,' ');if(!p)return 0;p++;}char*end;errno=0;unsigned long long v=strtoull(p,&end,10);return !errno&&end!=p?v:0;}
int main(int argc,char**argv){
 if(argc<4||argc>131||strlen(argv[1])>=sizeof(((struct sockaddr_un*)0)->sun_path)||strlen(argv[2])!=64||argv[3][0]!='/')return 64;
 size_t bytes=0;for(int i=3;i<argc;i++){size_t n=strlen(argv[i]);if(n>4096)return 64;bytes+=n+1;}if(bytes>32768)return 64;
 bytes=0;int envcount=0;for(char**e=environ;*e;e++){size_t n=strlen(*e);if(n>4096||++envcount>128)return 64;bytes+=n+1;}if(bytes>32768)return 64;
 for(int i=0;i<32;i++){char a=argv[2][2*i],b=argv[2][2*i+1];if(!((a>='0'&&a<='9')||(a>='a'&&a<='f'))||!((b>='0'&&b<='9')||(b>='a'&&b<='f')))return 64;nonce[i]=(unsigned char)((a<='9'?a-'0':a-'a'+10)*16+(b<='9'?b-'0':b-'a'+10));}
 signal(SIGPIPE,SIG_IGN);
 struct sigaction reapable={.sa_handler=SIG_DFL};sigemptyset(&reapable.sa_mask);if(sigaction(SIGCHLD,&reapable,NULL))return 70;
 control=socket(AF_UNIX,SOCK_SEQPACKET|SOCK_CLOEXEC,0);if(control<0)return 70;struct sockaddr_un a={.sun_family=AF_UNIX};strcpy(a.sun_path,argv[1]);if(connect(control,(struct sockaddr*)&a,sizeof a))return 70;
 struct ucred peer;socklen_t plen=sizeof peer;if(getsockopt(control,SOL_SOCKET,SO_PEERCRED,&peer,&plen)||plen!=sizeof peer||peer.uid!=getuid())return 70;
 int release[2],errors[2];if(pipe2(release,O_CLOEXEC)||pipe2(errors,O_CLOEXEC))return 70;
 pid_t parent=getpid();struct clone_args ca={.flags=CLONE_PIDFD,.pidfd=(uint64_t)(uintptr_t)&childfd,.exit_signal=SIGCHLD};long r=syscall(SYS_clone3,&ca,sizeof ca);
 if(r<0){packet(FAILED,2,(unsigned)errno,0);return 71;}
 if(r==0){
  close(release[1]);close(errors[0]);close(control);
  sigset_t empty;sigemptyset(&empty);if(sigprocmask(SIG_SETMASK,&empty,NULL))_exit(127);
  struct sigaction normal={.sa_handler=SIG_DFL};sigemptyset(&normal.sa_mask);if(sigaction(SIGPIPE,&normal,NULL))_exit(127);
  char go;ssize_t n;do{n=read(release[0],&go,1);}while(n<0&&errno==EINTR);close(release[0]);
  int error=0;if(n!=1||go!=1)error=ECANCELED;else if(prctl(PR_SET_PDEATHSIG,SIGKILL)||getppid()!=parent)error=ECHILD;else{execve(argv[3],argv+3,environ);error=errno;}
  const char*p=(const char*)&error;size_t left=sizeof error;while(left){ssize_t w=write(errors[1],p,left);if(w<0&&errno==EINTR)continue;if(w<=0)break;p+=w;left-=w;}_exit(127);
 }
 child=(pid_t)r;close(release[0]);close(errors[1]);siginfo_t info={0};int status=72;int64_t deadline=now()+10000;
 ticks=start_ticks(child);if(!ticks||childfd<0||packet(CREATED,1,0,1)||ready(control,deadline)<=0||receive(GO,1))goto abort;
 {char go=1;if(write(release[1],&go,1)!=1)goto abort;}close(release[1]);release[1]=-1;
 if(ready(errors[0],deadline)<=0)goto abort;
 {int error=0;size_t have=0;for(;;){ssize_t n=read(errors[0],((char*)&error)+have,sizeof error-have);if(n<0&&errno==EINTR)continue;if(n<0)goto abort;if(n==0){if(have)goto abort;break;}have+=(size_t)n;if(have==sizeof error){packet(FAILED,2,(unsigned)error,0);goto abort;}}}
 close(errors[0]);errors[0]=-1;if(packet(STARTED,2,0,0))goto abort;
 for(;;){struct pollfd ps[2]={{control,POLLIN,0},{childfd,POLLIN,0}};int n=poll(ps,2,-1);if(n<0&&errno==EINTR)continue;if(n<0)goto abort;
  if(ps[1].revents){if(reap(&info,1000))goto abort;status=0;goto done;}
  if(ps[0].revents){if(receive(STOP,2))goto abort;if(drain(&info))goto unsafe;status=0;goto done;}}
 abort: if(release[1]>=0){close(release[1]);release[1]=-1;}if(drain(&info))goto unsafe;
 done: if(info.si_code!=CLD_EXITED&&info.si_code!=CLD_KILLED&&info.si_code!=CLD_DUMPED)goto unsafe;if(info.si_status<0||info.si_status>255)goto unsafe;packet(EXITED,3,((unsigned)info.si_code<<16)|(unsigned)info.si_status,0);close(childfd);close(control);if(errors[0]>=0)close(errors[0]);return status;
 unsafe: /* No success receipt if original pidfd drain/reap cannot be proved. */return 73;
}
