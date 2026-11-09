#pragma
#include <errno.h>
#include <Platform/retarget.h>
#include "usart.h"
#include <stdio.h>
__asm(".global __use_no_semihosting");

UART_HandleTypeDef *gHuart;

#ifndef EBADF
#define EBADF  9
#endif
#ifndef EIO
#define EIO    5
#endif

int fputc(int ch, FILE *f) {
    (void)f;
    if (gHuart) HAL_UART_Transmit(gHuart, (uint8_t*)&ch, 1, HAL_MAX_DELAY);
    return ch;
}
int fgetc(FILE *f) {
    (void)f;
    uint8_t ch = 0;
    if (gHuart) HAL_UART_Receive(gHuart, &ch, 1, HAL_MAX_DELAY);
    return (int)ch;
}

#if !defined(OS_USE_SEMIHOSTING)

#define STDIN_FILENO  0
#define STDOUT_FILENO 1
#define STDERR_FILENO 2

void RetargetInit(UART_HandleTypeDef *huart)
{
    gHuart = huart;

    /* Disable I/O buffering for STDOUT stream, so that
     * chars are sent out as soon as they are printed. */
    setvbuf(stdout, NULL, _IONBF, 0);
}

int _isatty(int fd)
{
    if (fd >= STDIN_FILENO && fd <= STDERR_FILENO)
        return 1;

    errno = EBADF;
    return 0;
}

int _write(int fd, char *ptr, int len)
{
    if (fd == STDOUT_FILENO || fd == STDERR_FILENO)
    {
        // ToDo: Change this to DMA version
        HAL_UART_Transmit(gHuart, (uint8_t *) ptr, len, HAL_MAX_DELAY);
        return len;
    } else
        return -1;
}

int _close(int fd)
{
    if (fd >= STDIN_FILENO && fd <= STDERR_FILENO)
        return 0;

    errno = EBADF;
    return -1;
}

int _lseek(int fd, int ptr, int dir)
{
    (void) fd;
    (void) ptr;
    (void) dir;

    errno = EBADF;
    return -1;
}

int _read(int fd, char *ptr, int len)
{
    HAL_StatusTypeDef hstatus;

    if (fd == STDIN_FILENO)
    {
        hstatus = HAL_UART_Receive(gHuart, (uint8_t *) ptr, 1, HAL_MAX_DELAY);
        if (hstatus == HAL_OK)
            return 1;
        else
            return EIO;
    }
    errno = EBADF;
    return -1;
}

int _fstat(int fd, struct stat *st)
{
    if (fd >= STDIN_FILENO && fd <= STDERR_FILENO)
    {
        st->st_mode = S_IFCHR;
        return 0;
    }

    errno = EBADF;
    return 0;
}

void _ttywrch(int ch) {
    (void)ch;
}

/* 禁用 semihosting 后，标准 C 库需要这些桩函数 */
void _sys_exit(int status) { (void)status; while(1); }
int _sys_open(const char *name, int openmode) { (void)name; (void)openmode; return 0; }
int _sys_close(int fh) { (void)fh; return 0; }
int _sys_write(int fh, const unsigned char *buf, int len, int mode) { (void)fh; (void)buf; (void)len; (void)mode; return 0; }
int _sys_read(int fh, unsigned char *buf, int len, int mode) { (void)fh; (void)buf; (void)len; (void)mode; return 0; }
int _sys_istty(int fh) { (void)fh; return 1; }
int _sys_seek(int fh, long pos) { (void)fh; (void)pos; return -1; }
long _sys_flen(int fh) { (void)fh; return 0; }

#endif //#if !defined(OS_USE_SEMIHOSTING)