cmake_minimum_required(VERSION 3.20)
project(ninetoonerootlauncher LANGUAGES C)
add_executable(home-root-launcher home-root-launcher.c)
target_compile_features(home-root-launcher PRIVATE c_std_11)
target_compile_options(home-root-launcher PRIVATE -Wall -Wextra -Werror -Wformat=2 -fstack-protector-strong -fPIE)
target_link_options(home-root-launcher PRIVATE -pie -Wl,-z,relro,-z,now,-z,noexecstack)
# A normal signed runtime-cohort member. Never install setuid or grant file caps.
enable_testing()
# This argument refusal occurs before credential/namespace/child operations.
add_test(NAME root-launcher-refuses-missing-original-protocol COMMAND home-root-launcher)
set_tests_properties(root-launcher-refuses-missing-original-protocol PROPERTIES WILL_FAIL TRUE)
