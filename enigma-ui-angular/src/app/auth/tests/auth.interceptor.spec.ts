import { TestBed, inject } from '@angular/core/testing';
import {
  HttpInterceptor,
  HttpRequest,
  HttpHandler,
  HttpResponse,
  HTTP_INTERCEPTORS,
} from '@angular/common/http';
import { AuthInterceptor } from '../auth-interceptor';
import { Observable, of } from 'rxjs';

describe('AuthInterceptor', () => {
  let interceptor: HttpInterceptor;
  let mockHttpHandler: jasmine.SpyObj<HttpHandler>;

  beforeEach(() => {
    const httpHandlerSpy = jasmine.createSpyObj('HttpHandler', ['handle']);

    TestBed.configureTestingModule({
      providers: [
        AuthInterceptor,
        { provide: HttpHandler, useValue: httpHandlerSpy },
      ],
    });

    interceptor = TestBed.inject(AuthInterceptor);
    mockHttpHandler = TestBed.inject(HttpHandler) as jasmine.SpyObj<HttpHandler>;
  });

  it('should add Authorization header with token', () => {
    const token = 'dummyToken';
    const request = new HttpRequest('GET', '/data');
    const headers = request.headers.set('Authorization', `Bearer ${token}`);
    const expectedRequest = request.clone({ headers });

    localStorage.setItem('token', token);
    mockHttpHandler.handle.and.returnValue(of(new HttpResponse()));

    interceptor.intercept(request, mockHttpHandler);

    expect(mockHttpHandler.handle).toHaveBeenCalledWith(expectedRequest);
  });

  it('should not add Authorization header without token', () => {
    const request = new HttpRequest('GET', '/data');

    localStorage.removeItem('token');
    mockHttpHandler.handle.and.returnValue(of(new HttpResponse()));

    interceptor.intercept(request, mockHttpHandler);

    expect(mockHttpHandler.handle).toHaveBeenCalledWith(request);
  });
});
