import { NgModule } from '@angular/core';
import { IfLoggedInDirective } from './directives/if-logged-in.directive';
import { IfNotLoggedInDirective } from './directives/if-not-logged-in.directive';
import { AuthService } from './auth.service';

@NgModule({
  imports: [
    IfLoggedInDirective,
    IfNotLoggedInDirective
  ],
  providers:[AuthService],
  exports: [IfLoggedInDirective, IfNotLoggedInDirective],
})
export class AuthDirectivesModule {}
